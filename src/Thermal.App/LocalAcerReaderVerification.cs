using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private static async Task VerifyParentImageEncoding(){
        string[] paths=[@"X:\ordinary folder\Thermal.exe",@"X:\O'Brien\Thermal.exe",@"X:\dollar$and`tick;name\Thermal.exe",@"X:\日本語\Thermal.exe",
            @"X:\case"+'\u2018'+";Write-Output INERT;#"+@"\Thermal.exe",@"X:\case"+'\u2019'+";Write-Output INERT;#"+@"\Thermal.exe",
            @"X:\case"+'\u201a'+";Write-Output INERT;#"+@"\Thermal.exe",@"X:\case"+'\u201b'+";Write-Output INERT;#"+@"\Thermal.exe"];
        var cases=new StringBuilder();
        foreach(string path in paths){
            string code=Convert.ToBase64String(Encoding.Unicode.GetBytes(LocalAcerReaderHost.ParentImageAssignment(path)));
            string expected=Convert.ToBase64String(Encoding.Unicode.GetBytes(path));
            cases.Append("@{Code='").Append(code).Append("';Expected='").Append(expected).Append("'},");
        }
        // Parse generated text only. Never invoke a parsed script block or path payload.
        string script="$ErrorActionPreference='Stop';$PSModuleAutoLoadingPreference='None';$cases=@("+cases.ToString().TrimEnd(',')+");"+"""
            foreach($case in $cases){
              $code=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($case.Code))
              $tokens=$null;$errors=$null
              $ast=[Management.Automation.Language.Parser]::ParseInput($code,[ref]$tokens,[ref]$errors)
              if($errors.Count -ne 0 -or $ast.EndBlock.Statements.Count -ne 1){throw 'Path changed the statement structure'}
              $assignment=$ast.EndBlock.Statements[0]
              if($assignment -isnot [Management.Automation.Language.AssignmentStatementAst] -or $assignment.Left.VariablePath.UserPath -cne 'parentImage'){throw 'Unexpected assignment'}
              $commands=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true))
              $literals=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -and $node.Value -ceq $case.Expected},$true))
              if($commands.Count -ne 0 -or $literals.Count -ne 1){throw 'Path escaped the encoded literal'}
              $actual=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($literals[0].Value))
              $expected=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($case.Expected))
              if($actual -cne $expected){throw 'Path did not round-trip'}
            }
            [Console]::WriteLine($cases.Count)
            """;
        var start=new ProcessStartInfo{FileName=LocalAcerReaderHost.HostPath,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new InvalidOperationException("Path parser did not start");
        try{
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if(process.ExitCode!=0||await output!=paths.Length.ToString() + Environment.NewLine||!string.IsNullOrWhiteSpace(await error))throw new InvalidOperationException("PowerShell path parser regression failed");
        }finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
    }
    private async Task VerifyLocalAcerReader(){
        string directory=Path.Combine(AppContext.BaseDirectory,"local-acer-reader-verification");Directory.CreateDirectory(directory);
        if(File.Exists(Path.Combine(directory,"result.json"))||File.Exists(Path.Combine(directory,"error.txt")))throw new InvalidOperationException("Fresh local reader QA directory required");
        using var reader=new LocalAcerReaderClient(true);
        void Step(string value)=>File.AppendAllText(Path.Combine(directory,"steps.txt"),DateTimeOffset.UtcNow.ToString("O")+" "+value+"\n");
        try{
            Step("path encoding parser regression");await VerifyParentImageEncoding();
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
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{SyntheticOnly=true,FirmwareCalls=0,Elevation=false,StartupPolicyVerified=true,ParentImageEncodingVerified=true,AutomaticStartupSuppressedInQaVerified=true,PeerIdentityVerified=true,PauseResumeVerified=true,CacheClearedVerified=true,ProcessExitVerified=true,IndependentWatchdogVerified=true},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(Path.Combine(directory,"error.txt"),ex.ToString());}
        Close();
    }
}
