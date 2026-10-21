namespace Thermal.Core;
public enum ReadingState { Available, Unavailable, Unsupported, AccessDenied, Paused, Stale }
public sealed record Reading(string Id, string Name, double? Temperature, double? Load, ReadingState State, string Detail, DateTimeOffset At, string Source="");
public sealed record Snapshot(DateTimeOffset At, double? CpuLoad, double UsedRamGb, double TotalRamGb, IReadOnlyList<Reading> Devices, AcerSample? Acer=null);
/// <summary>UI-thread-owned bounded history. Copies and ranges are cached until the next mutation.</summary>
public sealed class History {
    private readonly Queue<Snapshot> samples = new();
    private readonly Queue<Snapshot> recent=new();
    private IReadOnlyList<Snapshot>? view,recentView;
    private readonly Dictionary<(string Id,bool Load),(double? Min,double? Max)> ranges=new();
    private bool rangesValid;
    public IReadOnlyList<Snapshot> Samples => view ??= Array.AsReadOnly(samples.ToArray());
    /// <summary>Immutable cached last-minute window. Overview never copies the full retained session.</summary>
    public IReadOnlyList<Snapshot> RecentSamples=>recentView??=Array.AsReadOnly(recent.ToArray());
    public TimeSpan Retention {get;private set;}
    public History(TimeSpan? retention=null){SetRetention(retention??TimeSpan.FromHours(1));}
    public void SetRetention(TimeSpan retention){
        if(retention<TimeSpan.FromHours(1)||retention>TimeSpan.FromHours(4))throw new ArgumentOutOfRangeException(nameof(retention));
        Retention=retention;if(samples.Count>0)Trim(samples.Last().At);Invalidate();
    }
    private void Trim(DateTimeOffset end){
        while(samples.Count>(int)Retention.TotalSeconds || (samples.Count>0 && end-samples.Peek().At>Retention))samples.Dequeue();
        while(recent.Count>61||(recent.Count>0&&end-recent.Peek().At>TimeSpan.FromSeconds(60)))recent.Dequeue();
    }
    public void Add(Snapshot sample) {
        samples.Enqueue(sample);recent.Enqueue(sample);
        Trim(sample.At);
        Invalidate();
    }
    public void Clear(){samples.Clear();recent.Clear();Invalidate();}
    private void Invalidate(){view=null;recentView=null;rangesValid=false;}
    public (double? Min,double? Max) Range(string id,bool load=false){
        if(!rangesValid){
            ranges.Clear();
            foreach(var sample in samples){
                if(sample.TotalRamGb>0)Accumulate(("ram",true),100*sample.UsedRamGb/sample.TotalRamGb);
                // IReadOnlyList enumeration boxes an enumerator per historical sample.
                // Index directly: four-hour ranges must not allocate thousands of enumerators each tick.
                for(int index=0;index<sample.Devices.Count;index++){
                var reading=sample.Devices[index];
                // CPU load remains valid even when its temperature provider is unavailable.
                if(reading.State==ReadingState.Stale||reading.State==ReadingState.Paused)continue;
                if(reading.State==ReadingState.Available)Accumulate((reading.Id,false),reading.Temperature);
                if(reading.State is ReadingState.Available or ReadingState.Unavailable)Accumulate((reading.Id,true),reading.Load);
            }
            }
            rangesValid=true;
        }
        return ranges.TryGetValue((id,load),out var range)?range:(null,null);
    }
    private void Accumulate((string Id,bool Load) key,double? value){
        if(value is not double v||!double.IsFinite(v))return;
        if(ranges.TryGetValue(key,out var old))ranges[key]=(Math.Min(old.Min!.Value,v),Math.Max(old.Max!.Value,v));
        else ranges[key]=(v,v);
    }
}
