using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private async Task VerifyLoggingPerformance(){
        string directory=Path.Combine(AppContext.BaseDirectory,"logging-performance-verification");Directory.CreateDirectory(directory);
        try{
            interval=1;var results=new List<object>();using var gpu=await Task.Run(()=>new NvidiaTelemetry());
            foreach(bool enabled in new[]{false,true}){
                if(enabled)localLog.Start();var offers=new List<double>();var ui=new List<double>();var sampling=new List<double>();
                for(int i=0;i<12;i++){
                    var cadence=Stopwatch.StartNew();var readingTimer=Stopwatch.StartNew();
                    var sample=await Task.Run(()=>baseline.Sample(gpu.Sample()));readingTimer.Stop();sampling.Add(readingTimer.Elapsed.TotalMilliseconds);
                    var offer=Stopwatch.StartNew();localLog.TryWrite(sample);offer.Stop();offers.Add(offer.Elapsed.TotalMilliseconds);
                    var paint=Stopwatch.StartNew();latest=sample;history.Add(sample);Render();UpdateLayout();paint.Stop();if(i>0)ui.Add(paint.Elapsed.TotalMilliseconds);
                    await Task.Delay(Math.Max(1,1000-(int)cadence.Elapsed.TotalMilliseconds));
                }
                if(enabled)await localLog.StopAsync();offers.Sort();ui.Sort();
                results.Add(new{Logging=enabled,SamplingSeconds=1,Ticks=12,EnqueueMeanMs=offers.Average(),EnqueueP95Ms=offers[10],UiMeanMs=ui.Average(),UiP95Ms=ui[9],SamplingMeanMs=sampling.Average(),WrittenSamples=localLog.WrittenSamples,DroppedSamples=localLog.DroppedSamples});
            }
            if(localLog.WrittenSamples!=12||localLog.DroppedSamples!=0||localLog.Error!=null)throw new InvalidOperationException("One-second live logging lost readings");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{RealReadings=true,NoCpuDriverAccess=true,Results=results},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(Path.Combine(directory,"error.txt"),ex.ToString());}
        Close();
    }
}
