using System;
using System.IO;
using Microsoft.Win32;
using Thermal.Core;
namespace Thermal.App;
internal static class AcerModel {
    public static bool Qualified(){
        try{using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return AcerTelemetryProtocol.SupportedModel(key?.GetValue("SystemManufacturer") as string??"",key?.GetValue("SystemProductName") as string??"");}
        catch(Exception ex)when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException){return false;}
    }
}
