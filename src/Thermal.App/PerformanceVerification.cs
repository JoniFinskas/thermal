using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.Json;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private async Task VerifyPerformance(){
        string directory=System.IO.Path.Combine(AppContext.BaseDirectory,"performance-verification");
        System.IO.Directory.CreateDirectory(directory);
        foreach(string name in new[]{"result.json","error.txt"}){string file=System.IO.Path.Combine(directory,name);if(System.IO.File.Exists(file))System.IO.File.Delete(file);}
        try{
            interval=1;TitleLabel.Text="SYNTHETIC TEMPERATURES · performance test";
            int capacity=(int)history.Retention.TotalSeconds;DateTimeOffset now=DateTimeOffset.UtcNow-history.Retention;
            Snapshot Fixture(int i)=>new(now.AddSeconds(i),25,12,32,new Reading[]{
                new("cpu","Synthetic CPU",60+8*Math.Sin(i*.03),25,ReadingState.Available,"QA fixture",now.AddSeconds(i)),
                new("nvidia-0","Synthetic GPU",50+5*Math.Sin(i*.04),18,ReadingState.Available,"QA fixture",now.AddSeconds(i))});
            for(int i=0;i<capacity;i++)history.Add(Fixture(i));latest=history.Samples[^1];
            var results=new List<object>();int sequence=capacity;
            foreach(string view in Environment.GetCommandLineArgs().Contains("--overview-only")?new[]{"Overview"}:Environment.GetCommandLineArgs().Contains("--history-only")?new[]{"History"}:new[]{"Overview","Sensors","History"}){
                minutes=(int)history.Retention.TotalMinutes;Navigate(view);UpdateLayout();await Task.Delay(1000);
                var times=new List<double>();var allocations=new List<long>();
                var process=Process.GetCurrentProcess();TimeSpan cpu=process.TotalProcessorTime;var wall=Stopwatch.StartNew();
                for(int i=0;i<20;i++){
                    long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                    latest=Fixture(sequence++);history.Add(latest);Render();UpdateLayout();
                    // Force the same rendering path as pointer inspection, including the full configured history window.
                    foreach(var chart in charts){chart.InspectAt(.5);chart.Measure(new System.Windows.Size(500,220));chart.Arrange(new System.Windows.Rect(0,0,500,220));
                        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(500,220,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(chart);}
                    foreach(var spark in sparklines){spark.Measure(new System.Windows.Size(190,64));spark.Arrange(new System.Windows.Rect(0,0,190,64));
                        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(190,64,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(spark);}
                    watch.Stop();times.Add(watch.Elapsed.TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread()-before);
                    await Task.Delay(Math.Max(1,1000-(int)watch.Elapsed.TotalMilliseconds));
                }
                process.Refresh();times.Sort();results.Add(new{View=view,Ticks=times.Count,MeanUpdateMs=times.Average(),P95UpdateMs=times[(int)(times.Count*.95)-1],MaxUpdateMs=times.Max(),MeanUiAllocatedBytes=allocations.Average(),CpuOneCorePercent=(process.TotalProcessorTime-cpu).TotalSeconds/wall.Elapsed.TotalSeconds*100,PrivateMiB=process.PrivateMemorySize64/1048576d});
            }
            var metrics=new WindowsMetrics();using var gpu=new NvidiaTelemetry();var sampleTimes=new List<double>();
            for(int i=0;i<12;i++){var duration=await Task.Run(()=>{var timer=Stopwatch.StartNew();var reading=metrics.Sample(gpu.Sample());if(reading.TotalRamGb<=0)throw new InvalidOperationException("RAM reading unavailable");return timer.Elapsed.TotalMilliseconds;});sampleTimes.Add(duration);await Task.Delay(1000);}
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Ui=results,RealSamplingMeanMs=sampleTimes.Average(),RealSamplingMaxMs=sampleTimes.Max(),HistoryCount=history.Samples.Count,RenderedHistoryPoints=charts.Sum(c=>c.RenderedPointCount),LogicalProcessorCount=Environment.ProcessorCount},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception ex){System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"error.txt"),ex.ToString());Environment.ExitCode=1;}
        Close();
    }
}
