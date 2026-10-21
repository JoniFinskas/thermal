using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace Thermal.Core;

[SupportedOSPlatform("windows")]
public static class AcerPipe {
    public static NamedPipeServerStream Create(string nonce){
        if(!Guid.TryParseExact(nonce,"N",out _))throw new ArgumentException("Invalid pipe nonce",nameof(nonce));
        using var identity=WindowsIdentity.GetCurrent();string sid=identity.User?.Value??throw new InvalidOperationException("No local user SID");
        // Explicit user-only DACL and owner, first instance, no inherited handle, and kernel rejection of remote clients.
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("O:"+sid+"D:P(A;;GA;;;"+sid+")",1,out var descriptor,out _))throw new Win32Exception();
        try{
            var security=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=descriptor,Inherit=false};
            var handle=CreateNamedPipe(@"\\.\pipe\thermal-acer-"+nonce,0x40080003,8,1,4096,4096,0,ref security);
            if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
            try{return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);}catch{handle.Dispose();throw;}
        }finally{LocalFree(descriptor);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct SecurityAttributes {public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
    [DllImport("advapi32",EntryPoint="ConvertStringSecurityDescriptorToSecurityDescriptorW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("kernel32",EntryPoint="CreateNamedPipeW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafePipeHandle CreateNamedPipe(string name,uint openMode,uint pipeMode,uint instances,uint outputBuffer,uint inputBuffer,uint timeout,ref SecurityAttributes security);
    [DllImport("kernel32")]private static extern IntPtr LocalFree(IntPtr memory);
}
