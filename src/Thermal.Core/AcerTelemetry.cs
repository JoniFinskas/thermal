namespace Thermal.Core;

public sealed record AcerMetric(int? Value, ReadingState State);
public sealed record AcerSample(DateTimeOffset At,AcerMetric CpuTemperature,AcerMetric CpuFan,AcerMetric GpuFan,AcerMetric GpuTemperature,string Detail="") {
    public static AcerSample Missing(DateTimeOffset at,ReadingState state,string detail)=>new(at,new(null,state),new(null,state),new(null,state),new(null,state),detail);
    public AcerSample FreshAt(DateTimeOffset now)=>now<At-TimeSpan.FromSeconds(2)||now-At>TimeSpan.FromSeconds(8)?Missing(At,ReadingState.Stale,"Acer firmware readings are stale"):this;
}

/// <summary>Only the existing firmware's fixed telemetry getter is exposed by this transport.</summary>
public interface IAcerTelemetryTransport { ulong ReadTelemetry(uint input); }

public static class AcerTelemetryProtocol {
    public const string FirmwareGuid="7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56";
    public static bool SupportedModel(string manufacturer,string model)=>manufacturer.Equals("Acer",StringComparison.OrdinalIgnoreCase)&&model.Equals("Predator PHN16-72",StringComparison.OrdinalIgnoreCase);
    public static bool AllowedInput(uint input)=>input is 0 or 257 or 513 or 1537 or 2561;
    public static ushort SupportedSensors(ulong response){
        if((response&255)!=0)throw new InvalidDataException("Firmware capability status is not success");
        return (ushort)((response>>24)&65535);
    }
    public static AcerMetric Decode(ulong response,bool temperature){
        int value=(int)((response>>8)&65535);
        return (response&255)!=0||value>(temperature?125:20000)||(temperature&&value==0)?new(null,ReadingState.Unavailable):new(value,ReadingState.Available);
    }
    public static AcerSample Sample(IAcerTelemetryTransport transport,ushort supported,DateTimeOffset now){
        AcerMetric Get(int id,bool temperature){
            if((supported&(1<<(id-1)))==0)return new(null,ReadingState.Unsupported);
            return Decode(transport.ReadTelemetry((uint)(1|(id<<8))),temperature);
        }
        return new(now,Get(1,true),Get(2,false),Get(6,false),Get(10,true));
    }
    public static Snapshot Merge(Snapshot baseline,AcerSample? firmware){
        if(firmware==null)return baseline;
        firmware=firmware.FreshAt(baseline.At);
        // Preserve real Windows CPU load and every existing graphics reading.
        var devices=baseline.Devices.Select(d=>d.Id=="cpu"?
            d with {Temperature=firmware.CpuTemperature.Value,State=firmware.CpuTemperature.State==ReadingState.Available?ReadingState.Available:ReadingState.Unavailable,Detail=firmware.CpuTemperature.State==ReadingState.Available?"Acer CPU firmware temperature":firmware.Detail.Length>0?firmware.Detail:"Acer CPU firmware temperature unavailable",Source="acer-firmware"}:d).ToArray();
        return baseline with {Devices=Array.AsReadOnly(devices),Acer=firmware};
    }
}
