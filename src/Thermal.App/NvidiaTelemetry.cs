using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Thermal.Core;
namespace Thermal.App;
// Original ABI bindings: read-only calls to the NVIDIA API installed by its driver.
// No bundled vendor SDK/DLL, fan control, overclocking or caller-selected library.
public sealed class NvidiaTelemetry : IDisposable {
    private IntPtr library;
    private readonly List<(IntPtr Handle,string Name)> devices=new();
    private ThermalGet? temperature;
    private UtilGet? utilization;
    public NvidiaTelemetry() {
        string file=Path.Combine(Environment.SystemDirectory,Environment.Is64BitProcess?"nvapi64.dll":"nvapi.dll");
        if(!File.Exists(file))return;
        library=LoadLibraryExW(file,IntPtr.Zero,0x800);
        if(library==IntPtr.Zero)return;
        try {
            var entry=GetProcAddress(library,"nvapi_QueryInterface");if(entry==IntPtr.Zero)return;
            var query=Marshal.GetDelegateForFunctionPointer<Query>(entry);
            T? Function<T>(uint id) where T:Delegate {var pointer=query(id);return pointer==IntPtr.Zero?null:Marshal.GetDelegateForFunctionPointer<T>(pointer);}
            var initialize=Function<Init>(0x0150E828);var enumerate=Function<Enumerate>(0xE5AC921F);var name=Function<NameGet>(0xCEEE8E9F);
            temperature=Function<ThermalGet>(0xE3640A56);utilization=Function<UtilGet>(0x60DED2ED);
            if(initialize==null||enumerate==null||initialize()!=0)return;
            IntPtr[] handles=new IntPtr[64];if(enumerate(handles,out int count)!=0)return;
            for(int i=0;i<Math.Clamp(count,0,64);i++){var text=new StringBuilder(64);if(name==null||name(handles[i],text)!=0)text.Append("NVIDIA GPU");devices.Add((handles[i],text.ToString()));}
        } catch(ArgumentException){}catch(MarshalDirectiveException){}
    }
    public IReadOnlyList<Reading> Sample() {
        var values=new List<Reading>();int index=0;
        foreach(var device in devices) {
            double? temp=null,load=null;var now=DateTimeOffset.UtcNow;
            var thermal=new Thermal {Version=(uint)Marshal.SizeOf<Thermal>()|(2u<<16),Sensors=new Sensor[3]};
            if(temperature!=null&&temperature(device.Handle,0,ref thermal)==0){for(int i=0;i<Math.Min(thermal.Count,3);i++){if(thermal.Sensors[i].Target==1){temp=thermal.Sensors[i].Current;break;}}}
            var util=new Util {Version=(uint)Marshal.SizeOf<Util>()|(1u<<16),Entries=new Usage[8]};
            if(utilization!=null&&utilization(device.Handle,ref util)==0&&(util.Entries[0].Flags&1)!=0&&util.Entries[0].Percent<=100)load=util.Entries[0].Percent;
            values.Add(new("nvidia-"+index++,device.Name,temp,load,temp.HasValue?ReadingState.Available:ReadingState.Unavailable,temp.HasValue?"GPU core · NVIDIA driver API":"Temperature not exposed by the driver",now,"nvidia-nvapi"));
        }
        return values;
    }
    public void Dispose(){if(library!=IntPtr.Zero){FreeLibrary(library);library=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential,Pack=8)] private struct Sensor {public uint Controller,DefaultMin,DefaultMax,Current,Target;}
    [StructLayout(LayoutKind.Sequential,Pack=8)] private struct Thermal {public uint Version,Count;[MarshalAs(UnmanagedType.ByValArray,SizeConst=3)] public Sensor[] Sensors;}
    [StructLayout(LayoutKind.Sequential)] private struct Usage {public uint Flags,Percent;}
    [StructLayout(LayoutKind.Sequential)] private struct Util {public uint Version,Flags;[MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public Usage[] Entries;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out,MarshalAs(UnmanagedType.LPArray,SizeConst=64)]IntPtr[] handles,out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NameGet(IntPtr handle,StringBuilder name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ThermalGet(IntPtr handle,int index,ref Thermal settings);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UtilGet(IntPtr handle,ref Util settings);
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr LoadLibraryExW(string file,IntPtr reserved,uint flags);
    [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetProcAddress(IntPtr library,string name);
    [DllImport("kernel32")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool FreeLibrary(IntPtr library);
}
