using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private async Task VerifyLocalAcerReader(){
        string directory=Path.Combine(AppContext.BaseDirectory,"local-acer-reader-verification");Directory.CreateDirectory(directory);
        if(File.Exists(Path.Combine(directory,"result.json"))||File.Exists(Path.Combine(directory,"error.txt")))throw new InvalidOperationException("Fresh local reader QA directory required");
        using var reader=new LocalAcerReaderClient(true);
        void Step(string value)=>File.AppendAllText(Path.Combine(directory,"steps.txt"),DateTimeOffset.UtcNow.ToString("O")+" "+value+"\n");
        try{
            Step("startup policy");
            RequestAcerOnOpen();RequestAcerOnOpen();
            if(acerReader!=null||acerEnabled||acerStartupAttempted)throw new InvalidOperationException("QA requested automatic live access");
            async Task Until(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>20||reader.Completion.IsCompleted)throw new InvalidOperationException("Local reader fixture failed: "+reader.Status);await Task.Delay(100);}}
            foreach(string prefix in new[]{"COR_","CORECLR_","COMPLUS_","DOTNET_","APPDOMAIN_MANAGER_"})if(!LocalAcerReaderHost.SuspiciousStartup(prefix+"TEST","1")||LocalAcerReaderHost.SuspiciousStartup(prefix+"TEST",null))throw new InvalidOperationException("Startup policy mismatch");
            if(LocalAcerReaderHost.SuspiciousStartup("PATH","x"))throw new InvalidOperationException("Unrelated environment rejected");
            if(!LocalAcerReaderHost.SuspiciousRegistryName("appdomainmanagerassembly")||!LocalAcerReaderHost.SuspiciousRegistryName("APPDOMAINMANAGERTYPE")||LocalAcerReaderHost.SuspiciousRegistryName("InstallRoot"))throw new InvalidOperationException("Registry casing policy mismatch");
            try{LocalAcerReaderHost.StartInfo("invalid",true);throw new Exception("Bad nonce accepted");}catch(ArgumentException){}
            var start=LocalAcerReaderHost.StartInfo(Guid.NewGuid().ToString("N"),true);
            if(start.UseShellExecute||start.Verb!=""||!start.Arguments.StartsWith("-NoLogo -NoProfile -NonInteractive -EncodedCommand ")||start.Arguments.Length>30000)throw new InvalidOperationException("Unsafe fixture launch");
            Step("start reader");reader.Start();await Until(()=>reader.Latest!=null);var first=reader.Latest!;Step("first sample");
            if(!reader.Running||first.CpuTemperature.Value!=65||first.CpuFan.State!=ReadingState.Unsupported||first.GpuTemperature.State!=ReadingState.Unsupported)throw new InvalidOperationException("Wrong synthetic values");
            reader.Pause(true);if(reader.Latest!=null)throw new InvalidOperationException("Pause retained cached reading");await Until(()=>reader.Status.Contains("paused",StringComparison.Ordinal));
            await Task.Delay(2500);if(reader.Latest!=null)throw new InvalidOperationException("Paused reader published sample");
            Step("resume");reader.Pause(false);await Until(()=>reader.Latest!=null);if(reader.Latest!.Detail!="SYNTHETIC LOCAL - reads=2")throw new InvalidOperationException("Pause performed firmware-equivalent reads");
            reader.Dispose();await reader.Completion.WaitAsync(TimeSpan.FromSeconds(25));if(!reader.Settled||reader.Latest!=null)throw new InvalidOperationException("Reader exit unconfirmed");
            // No commands: the independent native timer must terminate an idle reader.
            Step("idle watchdog start");string nonce=Guid.NewGuid().ToString("N");using var idlePipe=AcerPipe.Create(nonce);
            using var idle=Process.Start(LocalAcerReaderHost.StartInfo(nonce,true))??throw new InvalidOperationException("Idle fixture did not start");
            await idlePipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Step("idle connected");
            await idle.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));if(idle.ExitCode!=7)throw new InvalidOperationException("Independent idle watchdog failed");
            Step("idle watchdog passed");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{SyntheticOnly=true,FirmwareCalls=0,Elevation=false,StartupPolicyVerified=true,AutomaticStartupSuppressedInQaVerified=true,PeerIdentityVerified=true,PauseResumeVerified=true,CacheClearedVerified=true,ProcessExitVerified=true,IndependentWatchdogVerified=true},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(Path.Combine(directory,"error.txt"),ex.ToString());}
        Close();
    }
}
