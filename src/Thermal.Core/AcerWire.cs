using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace Thermal.Core;

/// <summary>Length-bounded local messages; no paths, method names or firmware arguments cross the pipe.</summary>
public static class AcerWire {
    public const int MaximumBytes=4096;
    public static async Task Write(PipeStream pipe,AcerSample sample,CancellationToken token){
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(sample);
        if(data.Length>MaximumBytes)throw new InvalidDataException("Oversize sensor message");
        await pipe.WriteAsync(BitConverter.GetBytes(data.Length),token);await pipe.WriteAsync(data,token);await pipe.FlushAsync(token);
    }
    public static async Task<AcerSample> Read(PipeStream pipe,CancellationToken token){
        byte[] header=new byte[4];await pipe.ReadExactlyAsync(header,token);int length=BitConverter.ToInt32(header);
        if(length<1||length>MaximumBytes)throw new InvalidDataException("Invalid sensor message size");
        byte[] data=new byte[length];await pipe.ReadExactlyAsync(data,token);
        var value=JsonSerializer.Deserialize<AcerSample>(data)??throw new InvalidDataException("Empty sensor message");
        bool Valid(AcerMetric? m,bool temperature)=>m!=null&&Enum.IsDefined(m.State)&&(m.State==ReadingState.Available?
            m.Value is int n&&n>=(temperature?1:0)&&n<=(temperature?125:20000):m.Value==null);
        if(!Valid(value.CpuTemperature,true)||!Valid(value.GpuTemperature,true)||!Valid(value.CpuFan,false)||!Valid(value.GpuFan,false)||value.Detail==null||value.Detail.Length>256)throw new InvalidDataException("Invalid sensor message");
        return value;
    }
    public static uint ClientPid(PipeStream pipe){if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid))throw new IOException("Cannot identify sensor pipe client");return pid;}
    public static uint ServerPid(PipeStream pipe){if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint pid))throw new IOException("Cannot identify sensor pipe server");return pid;}
    public static uint ClientSession(PipeStream pipe){if(!GetNamedPipeClientSessionId(pipe.SafePipeHandle,out uint id))throw new IOException("Cannot identify client session");return id;}
    public static uint ServerSession(PipeStream pipe){if(!GetNamedPipeServerSessionId(pipe.SafePipeHandle,out uint id))throw new IOException("Cannot identify server session");return id;}
    public static string ImagePath(Process process){var text=new StringBuilder(32768);uint length=(uint)text.Capacity;if(!QueryFullProcessImageName(process.SafeHandle,0,text,ref length))throw new IOException("Cannot verify process image");return text.ToString();}
    [DllImport("kernel32",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref uint length);
    [DllImport("kernel32",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe,out uint id);
    [DllImport("kernel32",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe,out uint id);
    [DllImport("kernel32",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
}
