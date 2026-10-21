using System.Globalization;
using System.Text;

namespace Thermal.Core;

public static class TelemetryCsv {
    public const string Header="timestamp_utc,device_id,device_name,temperature_c,load_percent,ram_used_gib,ram_total_gib,ram_usage_percent,state,fan_rpm,source";
    public static string TextCell(string value){
        // Hardware labels are data; avoid spreadsheet formula interpretation.
        value=value.Length>512?value[..512]:value;
        var leading=value.TrimStart();if(leading.Length>0&&"=+-@".Contains(leading[0]))value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    private static string Number(double? value)=>value is double n&&double.IsFinite(n)?n.ToString("0.###",CultureInfo.InvariantCulture):"";
    public static IEnumerable<string> Rows(Snapshot sample){
        string at=sample.At.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
        for(int i=0;i<Math.Min(sample.Devices.Count,64);i++){
            var d=sample.Devices[i];bool load=d.State is ReadingState.Available or ReadingState.Unavailable;
            yield return string.Join(",",at,TextCell(d.Id),TextCell(d.Name),Number(d.State==ReadingState.Available?d.Temperature:null),Number(load?d.Load:null),"","","",d.State.ToString(),"",TextCell(d.Source));
        }
        yield return string.Join(",",at,TextCell("ram"),TextCell("Physical RAM"),"","",Number(sample.UsedRamGb),Number(sample.TotalRamGb),Number(sample.TotalRamGb>0?100*sample.UsedRamGb/sample.TotalRamGb:null),"Available","",TextCell("windows"));
        if(sample.Acer is AcerSample firmware)foreach(var row in FirmwareRows(at,firmware.FreshAt(sample.At)))yield return row;
    }
    private static IEnumerable<string> FirmwareRows(string at,AcerSample value){
        string Row(string id,string name,AcerMetric m,bool temperature)=>string.Join(",",at,TextCell(id),TextCell(name),temperature&&m.State==ReadingState.Available?Number(m.Value):"","","","","",m.State.ToString(),!temperature&&m.State==ReadingState.Available?Number(m.Value):"",TextCell("acer-firmware"));
        yield return Row("acer-cpu-fan","CPU fan",value.CpuFan,false);yield return Row("acer-gpu-fan","GPU fan",value.GpuFan,false);yield return Row("acer-gpu-firmware","GPU firmware temperature",value.GpuTemperature,true);
    }
    public static async Task ExportAsync(string destination,IReadOnlyList<Snapshot> samples,CancellationToken cancellation=default){
        string target=Path.GetFullPath(destination),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,16384,FileOptions.Asynchronous|FileOptions.SequentialScan)){
                await using var writer=new StreamWriter(file,new UTF8Encoding(true),16384){NewLine="\r\n"};
                await writer.WriteLineAsync(Header.AsMemory(),cancellation);
                foreach(var sample in samples)foreach(string row in Rows(sample))await writer.WriteLineAsync(row.AsMemory(),cancellation);
                await writer.FlushAsync(cancellation);
            }
            cancellation.ThrowIfCancellationRequested();File.Move(temporary,target,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
