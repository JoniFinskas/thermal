using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Thermal.Core;

public sealed record LogLimits(int QueueCapacity=128,long FileBytes=10*1024*1024,long TotalBytes=100*1024*1024,int FileCount=10,int RetentionDays=7);

/// <summary>One background writer; producers only make bounded, nonblocking queue offers.</summary>
public sealed class LocalCsvLog {
    private readonly string directory;
    private readonly LogLimits limits;
    private readonly object gate=new();
    private Session? active;
    private string? error;
    private long written,dropped;
    public string DirectoryPath=>directory;
    public string? Error=>Volatile.Read(ref error);
    public long WrittenSamples=>Interlocked.Read(ref written);
    public long DroppedSamples=>Interlocked.Read(ref dropped);
    public bool IsEnabled=>Volatile.Read(ref active)?.Accepting==true;
    public LocalCsvLog(string directory,LogLimits? limits=null){
        this.directory=Path.GetFullPath(directory);this.limits=limits??new();
        if(this.limits.QueueCapacity<1||this.limits.QueueCapacity>1024||this.limits.FileBytes<512||this.limits.TotalBytes<this.limits.FileBytes||this.limits.FileCount<1||this.limits.RetentionDays<1)throw new ArgumentOutOfRangeException(nameof(limits));
    }
    public void Start(){
        lock(gate){
            if(active!=null&&!active.Worker.IsCompleted)throw new InvalidOperationException("Previous logging session is still finishing");
            Volatile.Write(ref error,null);Interlocked.Exchange(ref written,0);Interlocked.Exchange(ref dropped,0);
            var session=new Session(limits.QueueCapacity);active=session;session.Worker=Task.Run(()=>WriteSession(session));
        }
    }
    public bool TryWrite(Snapshot sample){
        var session=Volatile.Read(ref active);if(session?.Accepting!=true)return false;
        if(session.Queue.Writer.TryWrite(sample))return true;Interlocked.Increment(ref dropped);return false;
    }
    public Task StopAsync(){
        lock(gate){if(active==null)return Task.CompletedTask;active.Accepting=false;active.Queue.Writer.TryComplete();return active.Worker;}
    }
    public void Abort(){lock(gate){if(active==null)return;active.Accepting=false;active.Queue.Writer.TryComplete();active.Cancel.Cancel();}}
    private async Task WriteSession(Session session){
        StreamWriter? writer=null;FileStream? file=null;string? current=null;long bytes=0;
        async Task CloseFile(){if(writer!=null){await writer.DisposeAsync();writer=null;file=null;}}
        async Task OpenFile(){
            Directory.CreateDirectory(directory);
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Log directory cannot be a redirected filesystem entry");
            current=Path.Combine(directory,"thermal-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'",System.Globalization.CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")+".csv");
            file=new FileStream(current,FileMode.CreateNew,FileAccess.Write,FileShare.Read,16384,FileOptions.Asynchronous|FileOptions.SequentialScan);
            writer=new StreamWriter(file,new UTF8Encoding(true),16384){NewLine="\r\n"};
            await writer.WriteLineAsync(TelemetryCsv.Header.AsMemory(),session.Cancel.Token);await writer.FlushAsync(session.Cancel.Token);bytes=file.Position;
            Prune(current,bytes);
        }
        try{
            await OpenFile();
            await foreach(var sample in session.Queue.Reader.ReadAllAsync(session.Cancel.Token)){
                foreach(string row in TelemetryCsv.Rows(sample)){
                    long size=Encoding.UTF8.GetByteCount(row)+2;
                    if(bytes+size>limits.FileBytes){await CloseFile();await OpenFile();}
                    await writer!.WriteLineAsync(row.AsMemory(),session.Cancel.Token);bytes+=size;
                }
                await writer!.FlushAsync(session.Cancel.Token);Interlocked.Increment(ref written);
            }
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or OperationCanceledException){
            if(ex is not OperationCanceledException)Volatile.Write(ref error,"Logging stopped: check disk space and access to the local log folder.");
        }finally{
            session.Accepting=false;session.Queue.Writer.TryComplete();
            try{await CloseFile();if(current!=null)Prune(current,bytes);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Volatile.Write(ref error,"Logging stopped: could not finish the local file.");}
        }
    }
    private static readonly Regex OwnedName=new("^thermal-[0-9]{8}T[0-9]{9}Z-[a-f0-9]{32}\\.csv$",RegexOptions.CultureInvariant);
    private void Prune(string current,long currentBytes){
        // Only this app's exact filename namespace, in this one directory; never recurse.
        var files=new DirectoryInfo(directory).EnumerateFiles("thermal-*.csv",SearchOption.TopDirectoryOnly)
            .Where(f=>OwnedName.IsMatch(f.Name)&&(f.Attributes&FileAttributes.ReparsePoint)==0&&f.FullName!=current).OrderBy(f=>f.LastWriteTimeUtc).ToList();
        long total=currentBytes+files.Sum(f=>f.Length);int count=files.Count+1;
        foreach(var f in files){
            if(f.LastWriteTimeUtc>=DateTime.UtcNow.AddDays(-limits.RetentionDays)&&count<=limits.FileCount&&total<=limits.TotalBytes-limits.FileBytes)continue;
            long length=f.Length;f.Delete();total-=length;count--;
        }
    }
    private sealed class Session {
        public readonly Channel<Snapshot> Queue;
        public readonly CancellationTokenSource Cancel=new();
        public volatile bool Accepting=true;
        public Task Worker=Task.CompletedTask;
        public Session(int capacity){Queue=Channel.CreateBounded<Snapshot>(new BoundedChannelOptions(capacity){SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});}
    }
}
