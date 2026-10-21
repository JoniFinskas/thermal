using Thermal.Core;
int count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
Check(!typeof(Snapshot).Assembly.GetTypes().Any(t=>t.Name is "IMsrTransport" or "PawnIoTransport" or "CpuDriverAdmission"),"Public Core must exclude the CPU-driver experiment");
Check(AcerTelemetryProtocol.SupportedModel("Acer","Predator PHN16-72"),"Qualified Acer model remains available");
foreach(var model in new[]{"", "Predator PHN16-71", "Other"})Check(!AcerTelemetryProtocol.SupportedModel("Acer",model),"Do not claim unqualified Acer support");
Check(!AcerTelemetryProtocol.SupportedModel("Other","Predator PHN16-72"),"Manufacturer must match");
foreach(string value in new[]{"=1+1"," +1","-1","@SUM(1)","\t=1"})Check(TelemetryCsv.TextCell(value).StartsWith("\"'"),"CSV labels must not become spreadsheet formulas");
var at=DateTimeOffset.UtcNow;
var missing=AcerSample.Missing(at,ReadingState.Unavailable,"Not enabled");
var snapshot=new Snapshot(at,25,8,32,new[]{new Reading("cpu","CPU",null,25,ReadingState.Unavailable,"Optional Acer temperature",at)});
var baseline=AcerTelemetryProtocol.Merge(snapshot,null);
Check(baseline.CpuLoad==25&&baseline.UsedRamGb==8&&baseline.Devices[0].Temperature==null,"Declining Acer must preserve ordinary readings");
var sample=missing with{CpuTemperature=new(65,ReadingState.Available)};
Check(AcerTelemetryProtocol.Merge(snapshot,sample).Devices[0].Temperature==65,"Qualified firmware temperature merges separately from CPU load");
Check(sample.FreshAt(at.AddSeconds(9)).CpuTemperature.State==ReadingState.Stale,"Old firmware samples expire");
var history=new History(TimeSpan.FromHours(1));for(int i=0;i<3700;i++)history.Add(snapshot with{At=at.AddSeconds(i)});
Check(history.Samples.Count<=3600&&history.RecentSamples.Count<=61,"Retained history and overview are bounded");
Console.WriteLine($"{count} public behaviour checks passed; no driver, service, firmware or elevation used.");
