using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Thermal.Core;
namespace Thermal.App;

internal sealed class LocalAcerReaderClient:IDisposable {
    private readonly CancellationTokenSource cancel=new();
    private readonly bool fixture;
    private readonly object stateLock=new();
    private NamedPipeServerStream? pipe;
    private Process? helper;
    private AcerSample? sample;
    private int paused,generation;
    private string status="Starting CPU temperature reader…";
    public LocalAcerReaderClient(bool fixture=false){
        if(fixture&&!Environment.GetCommandLineArgs().Contains("--verify-local-acer-reader"))throw new InvalidOperationException("Explicit local QA entrypoint required.");
        this.fixture=fixture;
    }
    public AcerSample? Latest=>Volatile.Read(ref sample);
    public string Status=>Volatile.Read(ref status);
    public bool Running{get;private set;}
    public bool Settled{get;private set;}=true;
    public Task Completion{get;private set;}=Task.CompletedTask;
    public void Start(){if(!Settled)throw new InvalidOperationException("Reader already starting.");Settled=false;Completion=Run();}
    public void Pause(bool value){lock(stateLock){Volatile.Write(ref paused,value?1:0);Interlocked.Increment(ref generation);Volatile.Write(ref sample,null);}}
    private async Task Run(){
        try{
            string nonce=Guid.NewGuid().ToString("N");
            pipe=AcerPipe.Create(nonce);
            Volatile.Write(ref status,fixture?"Starting synthetic reader…":"Approve Windows access for CPU temperature. Nothing will be installed.");
            // Preflight and UAC may wait; neither runs on the dispatcher.
            helper=await Task.Run(()=>Process.Start(LocalAcerReaderHost.StartInfo(nonce,fixture)));
            cancel.Token.ThrowIfCancellationRequested();
            if(helper==null)throw new IOException("Reader did not start.");
            await pipe.WaitForConnectionAsync(cancel.Token).WaitAsync(TimeSpan.FromSeconds(10),cancel.Token);
            using(var current=Process.GetCurrentProcess())
                if(helper.HasExited||AcerWire.ClientPid(pipe)!=(uint)helper.Id||AcerWire.ClientSession(pipe)!=(uint)current.SessionId||!string.Equals(AcerWire.ImagePath(helper),LocalAcerReaderHost.HostPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected local reader identity.");
            Running=true;
            while(!cancel.IsCancellationRequested){
                int epoch=Volatile.Read(ref generation);bool reading=Volatile.Read(ref paused)==0;
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);timeout.CancelAfter(TimeSpan.FromSeconds(12));
                await pipe.WriteAsync(new[]{reading?(byte)1:(byte)2},timeout.Token);await pipe.FlushAsync(timeout.Token);
                if(reading){
                    var value=await AcerWire.Read(pipe,timeout.Token);
                    if(value.CpuTemperature.State!=ReadingState.Available||value.CpuFan.State!=ReadingState.Unsupported||value.GpuFan.State!=ReadingState.Unsupported||value.GpuTemperature.State!=ReadingState.Unsupported||(!fixture&&value.Detail!=LocalAcerReaderHost.CpuOnlyDetail))throw new InvalidDataException("Unexpected CPU-only sensor response.");
                    lock(stateLock){if(epoch==Volatile.Read(ref generation)&&Volatile.Read(ref paused)==0&&!cancel.IsCancellationRequested){Volatile.Write(ref sample,value);Volatile.Write(ref status,"CPU temperature · 2s refresh");}}
                }else{
                    byte[] ack=new byte[1];await pipe.ReadExactlyAsync(ack,timeout.Token);
                    if(ack[0]!=2)throw new InvalidDataException("Invalid pause reply.");
                    Volatile.Write(ref status,"CPU temperature readings paused.");
                }
                await Task.Delay(2100,cancel.Token);
            }
        }catch(System.ComponentModel.Win32Exception ex)when(ex.NativeErrorCode==1223){Volatile.Write(ref status,"Access cancelled. Other readings continue.");}
        catch(OperationCanceledException){Volatile.Write(ref status,cancel.IsCancellationRequested?"CPU temperature reader stopped.":"CPU temperature reader timed out.");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or TimeoutException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException){Volatile.Write(ref status,fixture?"Synthetic reader failed: "+ex.GetType().Name+": "+ex.Message:ex is InvalidOperationException?ex.Message:"CPU temperature unavailable. Other readings continue.");}
        finally{
            Running=false;Volatile.Write(ref sample,null);pipe?.Dispose();bool exited=true;
            if(helper!=null){try{await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}catch(Exception ex)when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException){exited=false;Volatile.Write(ref status,"Previous reader exit is unconfirmed. Close Thermal before retrying.");}}
            if(exited)helper?.Dispose();Settled=exited;
        }
    }
    internal (double CpuSeconds,long PrivateBytes,long WorkingBytes) GetResourceUsage(){
        var process=helper??throw new InvalidOperationException("Reader has not started");
        process.Refresh();return(process.TotalProcessorTime.TotalSeconds,process.PrivateMemorySize64,process.WorkingSet64);
    }
    public void Dispose(){lock(stateLock){cancel.Cancel();Interlocked.Increment(ref generation);Volatile.Write(ref sample,null);}pipe?.Dispose();}
}
