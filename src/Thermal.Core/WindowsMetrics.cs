using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
namespace Thermal.Core;
public sealed class WindowsMetrics {
    private ulong lastIdle,lastKernel,lastUser;
    private bool initialized;
    public string CpuName {get;}=GetCpuName();
    private readonly List<string> adapters=FindAdapters();
    public Snapshot Sample(IReadOnlyList<Reading> gpu) {
        double? busy=null;
        if(GetActiveProcessorCount(0xffff)<=64 && GetSystemTimes(out ulong idle,out ulong kernel,out ulong user)) {
            if(initialized && kernel>=lastKernel && user>=lastUser && idle>=lastIdle) {
                ulong total=(kernel-lastKernel)+(user-lastUser), rest=idle-lastIdle;
                if(total>0 && rest<=total) busy=100d*(total-rest)/total;
            }
            lastIdle=idle;lastKernel=kernel;lastUser=user;initialized=true;
        }
        var ram=new MemoryStatus {Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(!GlobalMemoryStatusEx(ref ram)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var now=DateTimeOffset.UtcNow;
        var devices=new List<Reading> {new("cpu",CpuName,null,busy,ReadingState.Unavailable,"Temperature setup not yet qualified",now)};
        devices.AddRange(gpu);
        int index=0;
        foreach(string adapter in adapters) {
            if(!gpu.Any(g=>string.Equals(g.Name,adapter,StringComparison.OrdinalIgnoreCase))) devices.Add(new("display-"+index,adapter,null,null,ReadingState.Unsupported,"Readings not exposed by a supported provider in this build",now));
            index++;
        }
        return new(now,busy,(ram.TotalPhysical-ram.AvailablePhysical)/1073741824d,ram.TotalPhysical/1073741824d,devices);
    }
    public void Reset()=>initialized=false;
    public static string GetCpuName() {
        if(!X86Base.IsSupported) return "CPU";
        var max=X86Base.CpuId(unchecked((int)0x80000000),0);
        if((uint)max.Eax<0x80000004) return "CPU";
        byte[] name=new byte[48];
        for(int i=0;i<3;i++) {var x=X86Base.CpuId(unchecked((int)(0x80000002u+(uint)i)),0);int[] a=[x.Eax,x.Ebx,x.Ecx,x.Edx];Buffer.BlockCopy(a,0,name,i*16,16);}
        return Encoding.ASCII.GetString(name).Trim('\u0000',' ');
    }
    private static List<string> FindAdapters() {
        var result=new List<string>();
        for(uint i=0;i<64;i++) {
            var device=new DisplayDevice {Size=(uint)Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key=""};
            if(!EnumDisplayDevicesW(null,i,ref device,0))break;
            if((device.Flags&8)==0&&!string.IsNullOrWhiteSpace(device.Description)&&!result.Contains(device.Description))result.Add(device.Description);
        }
        return result;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct DisplayDevice {public uint Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;}
    [DllImport("user32",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevicesW(string? name,uint index,ref DisplayDevice device,uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,AvailableExtended;}
    [DllImport("kernel32",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32")] private static extern uint GetActiveProcessorCount(ushort group);
}
