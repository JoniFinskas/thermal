using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private async Task VerifyRenderStress(){
        string directory=Path.Combine(AppContext.BaseDirectory,"render-stress-verification");Directory.CreateDirectory(directory);
        foreach(string name in new[]{"result.json","error.txt"}){string path=Path.Combine(directory,name);if(File.Exists(path))File.Delete(path);}
        int checks=0;var clock=Stopwatch.StartNew();
        try{
            void Draw(TrendChart chart,string name){chart.Measure(new Size(chart.Width,chart.Height));chart.Arrange(new Rect(0,0,chart.Width,chart.Height));CaptureElement(chart,Path.Combine(directory,name+".png"));checks++;}
            var at=DateTimeOffset.UtcNow;
            foreach(double used in new[]{double.NaN,double.PositiveInfinity,-1d,101d}){
                var chart=new TrendChart{Width=500,Height=200,Minutes=1,Metric=SparkMetric.MemoryUsage,Samples=new[]{new Snapshot(at,1,used,100,Array.Empty<Reading>())}};
                Draw(chart,"invalid-memory-"+checks);
                if(chart.RenderedPointCount!=0)throw new InvalidOperationException("Invalid RAM reading was plotted: "+used);
            }
            var full=Enumerable.Range(0,14400).Select(i=>new Snapshot(at.AddSeconds(i-14399),25,i%101,100,
                Enumerable.Range(0,8).Select(n=>new Reading(n==0?"cpu":"nvidia-"+(n-1),new string('X',512),i%97==0?null:40+n+15*Math.Sin(i*.02+n),i%101,i%97==0?ReadingState.Unavailable:ReadingState.Available,"Stress fixture",at.AddSeconds(i-14399))).ToArray())).ToArray();
            foreach(var metric in Enum.GetValues<SparkMetric>())foreach(int minutes in new[]{1,15,240})foreach(bool f in new[]{false,true}){
                var chart=new TrendChart{Width=500,Height=220,Minutes=minutes,Metric=metric,Fahrenheit=f,Samples=full};chart.InspectAt(.5);Draw(chart,"full-"+checks);
                if(chart.RenderedPointCount>8*4*(500*System.Windows.Media.VisualTreeHelper.GetDpi(chart).PixelsPerDip+2)+8*4*(14400/97+1))throw new InvalidOperationException("History draw work is not pixel bounded");
                if(clock.Elapsed.TotalSeconds>45)throw new TimeoutException("Owned render stress deadline");await Task.Delay(15);
            }
            if(acerReader!=null)throw new InvalidOperationException("Render stress started a live reader");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,HistorySamples=full.Length,Devices=8,Seconds=clock.Elapsed.TotalSeconds,CpuHardwareCalls=0}));
        }catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(Path.Combine(directory,"error.txt"),ex.ToString());}
        Close();
    }
}
