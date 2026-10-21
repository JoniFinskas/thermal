using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
namespace Thermal.App;
internal sealed record SpecSection(string Title,string Icon,IReadOnlyList<(string Label,string Value)> Rows);
internal static class HardwareSpecs {
    // Local Windows inventory only: no serials, UUIDs, computer/user names or remote queries.
    public static IReadOnlyList<SpecSection> Detect(CancellationToken cancellation){
        var result=new List<SpecSection>();object? locator=null,service=null;
        try{
            var type=Type.GetTypeFromCLSID(new Guid("4590F811-1D3A-11D0-891F-00AA004B2E24"),true)!;
            locator=Activator.CreateInstance(type)!;
            Marshal.ThrowExceptionForHR(((IWbemLocator)locator).ConnectServer("ROOT\\CIMV2",null,null,null,128,null,IntPtr.Zero,out var connected));
            service=connected;
            Marshal.ThrowExceptionForHR(CoSetServicesSecurity((IWbemServices)service,10,0,null,6,3,IntPtr.Zero,0));
            Query("Processor","Cpu","Win32_Processor",["Name","NumberOfCores","NumberOfLogicalProcessors","MaxClockSpeed"],
                v=>[("Model",v[0]),("Cores / threads",v[1]+" cores · "+v[2]+" threads"),("Reported clock",v[3]+" MHz")]);
            Query("Memory modules","Memory","Win32_PhysicalMemory",["Capacity","Speed","SMBIOSMemoryType"],
                v=>[("Module",Bytes(v[0])+" · "+MemoryType(v[2])),("Reported speed",v[1]+" MHz")]);
            Query("Graphics","Gpu","Win32_VideoController",["Name","DriverVersion"],
                v=>[("Model",v[0]),("Driver",v[1])]);
            Query("Motherboard","Bios","Win32_BaseBoard",["Manufacturer","Product"],
                v=>[("Manufacturer",v[0]),("Model",v[1])]);
            Query("Storage","Storage","Win32_DiskDrive",["Model","Size","InterfaceType"],
                v=>[("Drive",v[0]),("Capacity / interface",Bytes(v[1])+" · "+v[2])]);
            Query("Windows","Display","Win32_OperatingSystem",["Caption","Version","OSArchitecture"],
                v=>[("Edition",v[0]),("Version / architecture",v[1]+" · "+v[2])]);
            Query("Firmware","Bios","Win32_BIOS",["Manufacturer","SMBIOSBIOSVersion"],
                v=>[("Vendor",v[0]),("BIOS version",v[1])]);
        }catch(Exception ex)when(ex is COMException or TypeLoadException or InvalidOperationException or System.Reflection.TargetInvocationException){
            result.Add(new("Windows inventory","Info",[("Status","Unavailable. Windows did not provide hardware inventory.")]));
        }finally{Release(service);Release(locator);}
        return result;
        void Query(string title,string icon,string table,string[] fields,Func<string[],(string,string)[]> format){
            cancellation.ThrowIfCancellationRequested();
            IEnumWbemClassObject? items=null;var rows=new List<(string,string)>();
            try{
                // Forward-only native WMI query; Next has a bounded wait, unlike automation enumeration.
                Marshal.ThrowExceptionForHR(((IWbemServices)service!).ExecQuery("WQL","SELECT "+string.Join(",",fields)+" FROM "+table,48,IntPtr.Zero,out items));
                Marshal.ThrowExceptionForHR(CoSetEnumerationSecurity(items,10,0,null,6,3,IntPtr.Zero,0));
                var batch=new IWbemClassObject[1];
                for(int count=0;count<64;count++){
                    cancellation.ThrowIfCancellationRequested();
                    int status=items.Next(1500,1,batch,out uint returned);
                    Marshal.ThrowExceptionForHR(status);
                    if(returned==0){
                        if(status==0x40004)throw new COMException("Inventory query timed out");
                        break;
                    }
                    var row=batch[0];
                    try{var values=new string[fields.Length];for(int i=0;i<fields.Length;i++)values[i]=Property(row,fields[i]);
                        if(rows.Count>0)rows.Add(("",""));rows.AddRange(format(values));}
                    finally{Release(row);batch[0]=null!;}
                }
                if(rows.Count==0)rows.Add(("Status","Not reported by Windows"));
            }catch(COMException){rows.Clear();rows.Add(("Status","Unavailable from Windows inventory"));}
            finally{Release(items);}
            result.Add(new(title,icon,rows));
        }
    }
    private static string Property(IWbemClassObject row,string field){
        if(row.Get(field,0,out object? raw,out _,out _) <0)return "Not reported";
        string value=Convert.ToString(raw,CultureInfo.InvariantCulture)?.Trim()??"";
        return value.Length==0||value=="0"?"Not reported":value[..Math.Min(value.Length,512)];
    }
    private static string Bytes(string value)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double size)&&size>0&&double.IsFinite(size)?(size/1073741824d).ToString("0.#",CultureInfo.CurrentCulture)+" GiB":"Capacity not reported";
    private static string MemoryType(string value)=>value switch{"24"=>"DDR3","26"=>"DDR4","34"=>"DDR5","30"=>"LPDDR4","35"=>"LPDDR5",_=>"Type not reported"};
    [DllImport("ole32",EntryPoint="CoSetProxyBlanket")]
    private static extern int CoSetServicesSecurity([MarshalAs(UnmanagedType.Interface)]IWbemServices proxy,uint authentication,uint authorization,[MarshalAs(UnmanagedType.LPWStr)]string? principal,uint level,uint impersonation,IntPtr identity,uint capabilities);
    [DllImport("ole32",EntryPoint="CoSetProxyBlanket")]
    private static extern int CoSetEnumerationSecurity([MarshalAs(UnmanagedType.Interface)]IEnumWbemClassObject proxy,uint authentication,uint authorization,[MarshalAs(UnmanagedType.LPWStr)]string? principal,uint level,uint impersonation,IntPtr identity,uint capabilities);
    [ComImport,Guid("DC12A687-737F-11CF-884D-00AA004B2E24"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWbemLocator {
        [PreserveSig]int ConnectServer([MarshalAs(UnmanagedType.BStr)]string path,[MarshalAs(UnmanagedType.BStr)]string? user,[MarshalAs(UnmanagedType.BStr)]string? password,[MarshalAs(UnmanagedType.BStr)]string? locale,int flags,[MarshalAs(UnmanagedType.BStr)]string? authority,IntPtr context,out IWbemServices services);
    }
    [ComImport,Guid("9556DC99-828C-11CF-A37E-00AA003240C7"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWbemServices {
        // Unused ABI slots precede ExecQuery; never invoke these placeholders.
        [PreserveSig]int OpenNamespace();[PreserveSig]int CancelAsyncCall();[PreserveSig]int QueryObjectSink();
        [PreserveSig]int GetObject();[PreserveSig]int GetObjectAsync();[PreserveSig]int PutClass();[PreserveSig]int PutClassAsync();
        [PreserveSig]int DeleteClass();[PreserveSig]int DeleteClassAsync();[PreserveSig]int CreateClassEnum();[PreserveSig]int CreateClassEnumAsync();
        [PreserveSig]int PutInstance();[PreserveSig]int PutInstanceAsync();[PreserveSig]int DeleteInstance();[PreserveSig]int DeleteInstanceAsync();
        [PreserveSig]int CreateInstanceEnum();[PreserveSig]int CreateInstanceEnumAsync();
        [PreserveSig]int ExecQuery([MarshalAs(UnmanagedType.BStr)]string language,[MarshalAs(UnmanagedType.BStr)]string query,int flags,IntPtr context,out IEnumWbemClassObject result);
    }
    [ComImport,Guid("027947E1-D731-11CE-A357-000000000001"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumWbemClassObject {
        [PreserveSig]int Reset();
        [PreserveSig]int Next(int timeout,uint count,[Out,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=1)]IWbemClassObject[] objects,out uint returned);
    }
    [ComImport,Guid("DC12A681-737F-11CF-884D-00AA004B2E24"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWbemClassObject {
        [PreserveSig]int GetQualifierSet();
        [PreserveSig]int Get([MarshalAs(UnmanagedType.LPWStr)]string name,int flags,[MarshalAs(UnmanagedType.Struct)]out object? value,out int type,out int flavor);
    }
    private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
}
