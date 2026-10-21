using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
namespace Thermal.App;

// Local-only policy for the already-verified Acer firmware. Legacy app-private
// elevated .NET helpers and all general CPU-driver gates remain closed.
internal static class LocalAcerReaderHost {
    public static bool Enabled=>true;
    public const string CpuOnlyDetail="Local CPU firmware reading";
    private const string HostHash="60c9b29843624dd8af6b6a7b26147753be6aeef65098b9a503cc50850afe410f";
    private const string ClrHash="121b975d4242a069be2f46e59b661b95c7c468e30c0a9820da1066d8e40df524";
    private static readonly string[] StartupPrefixes=["COR_","CORECLR_","COMPLUS_","DOTNET_","APPDOMAIN_MANAGER_"];
    public static bool SuspiciousStartup(string name,string? value)=>!string.IsNullOrEmpty(value)&&Array.Exists(StartupPrefixes,p=>name.StartsWith(p,StringComparison.OrdinalIgnoreCase));
    public static bool SuspiciousRegistryName(string name)=>Array.Exists(StartupPrefixes,p=>name.StartsWith(p,StringComparison.OrdinalIgnoreCase))||name.Equals("AppDomainManagerAssembly",StringComparison.OrdinalIgnoreCase)||name.Equals("AppDomainManagerType",StringComparison.OrdinalIgnoreCase);
    public static string HostPath=>Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
    public static void Preflight(){
        if(!OperatingSystem.IsWindows()||!Environment.Is64BitProcess||!Environment.Is64BitOperatingSystem)throw new InvalidOperationException("The local Windows reader requires 64-bit Windows.");
        foreach(EnvironmentVariableTarget target in Enum.GetValues<EnvironmentVariableTarget>())
            foreach(DictionaryEntry item in Environment.GetEnvironmentVariables(target))
                if(SuspiciousStartup((string)item.Key,item.Value as string))throw new InvalidOperationException("A runtime startup override prevents safe reader startup.");
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
                using var root=RegistryKey.OpenBaseKey(hive,view);
                using var key=root.OpenSubKey(@"SOFTWARE\Microsoft\.NETFramework");
                if(key==null)continue;
                foreach(string name in key.GetValueNames()){
                    if(!SuspiciousRegistryName(name))continue;
                    object? value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if(value!=null&&value.ToString()!=""&&value.ToString()!="0")throw new InvalidOperationException("A Framework startup override prevents safe reader startup.");
                }
            }
        using(var bios=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
            if(bios?.GetValue("BIOSVersion") is not string version||version!="V1.11"||!AcerModel.Qualified())throw new InvalidOperationException("This local CPU reader is qualified for this Acer model and BIOS only.");
        Verify(HostPath,HostHash);
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string clr=Path.Combine(windows,"Microsoft.NET","Framework64","v4.0.30319","clr.dll");
        Verify(clr,ClrHash);
        var file=FileVersionInfo.GetVersionInfo(clr);
        if(new Version(file.FileMajorPart,file.FileMinorPart,file.FileBuildPart,file.FilePrivatePart)<new Version(4,8,10008,0))throw new InvalidOperationException("Windows Framework servicing must be checked before reader startup.");
    }
    private static void Verify(string path,string expected){
        for(string? item=path;!string.IsNullOrEmpty(item)&&item!=Path.GetPathRoot(item);item=Path.GetDirectoryName(item))
            if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked Windows reader path rejected.");
        using var input=File.OpenRead(path);
        if(!Convert.ToHexString(SHA256.HashData(input)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Windows reader files changed; their signatures and servicing need re-verification.");
    }
    public static ProcessStartInfo StartInfo(string nonce,bool fixture){
        if(!Guid.TryParseExact(nonce,"N",out _))throw new ArgumentException("Invalid local pipe nonce.",nameof(nonce));
        if(fixture&&!Environment.GetCommandLineArgs().Contains("--verify-local-acer-reader"))throw new InvalidOperationException("Synthetic reader requires its explicit QA entrypoint.");
        if(!fixture)Preflight();
        string image=Environment.ProcessPath??throw new InvalidOperationException("Cannot identify Thermal.");
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Thermal.LocalAcerReader")??throw new InvalidOperationException("Local reader unavailable.");
        using var reader=new StreamReader(stream,Encoding.UTF8);
        string code="$parentId="+Environment.ProcessId+";$nonce='"+nonce+"';$parentImage='"+image.Replace("'","''")+"';$fixture="+(fixture?"$true":"$false")+";\n"+reader.ReadToEnd();
        string arguments="-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(code));
        if(arguments.Length>30000)throw new InvalidOperationException("Local reader command exceeds the Windows bound.");
        return new ProcessStartInfo {FileName=HostPath,WorkingDirectory=Environment.SystemDirectory,Arguments=arguments,UseShellExecute=!fixture,Verb=fixture?"":"runas",CreateNoWindow=fixture,WindowStyle=ProcessWindowStyle.Hidden};
    }
}
