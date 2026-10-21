using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using Thermal.Core;
namespace Thermal.App;
public enum SparkMetric {Temperature,Load,MemoryUsage}
/// <summary>At most 61 recent samples in one geometry. Explicit axes; no fabricated warm-up data.</summary>
public sealed class MetricSparkline:FrameworkElement {
    public IReadOnlyList<Snapshot> Samples{get;set;}=Array.Empty<Snapshot>();
    public string DeviceId{get;set;}="";
    public SparkMetric Metric{get;set;}
    public bool Fahrenheit{get;set;}
    private DrawingGroup? axes;
    private static readonly Typeface ChartTypeface=new("Segoe UI");
    private (double Width,double Height,double Low,double High,string Unit,double Dpi,bool Contrast) axesKey;
    public double? ScaleMinimum{get;private set;}
    public double? ScaleMaximum{get;private set;}
    public int PlottedSamples{get;private set;}
    protected override AutomationPeer OnCreateAutomationPeer()=>new SparkPeer(this);
    protected override void OnRender(DrawingContext dc){
        base.OnRender(dc);ScaleMinimum=null;ScaleMaximum=null;PlottedSamples=0;
        if(ActualWidth<75||ActualHeight<40)return;
        var muted=TryFindResource("Muted") as Brush??Brushes.LightGray;
        var ink=TryFindResource(Metric==SparkMetric.Temperature?"Accent":"Muted") as Brush??Brushes.White;
        var divider=TryFindResource("Divider") as Brush??Brushes.Gray;
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Label(string text,double x,double y,DrawingContext? target=null)=>(target??dc).DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,ChartTypeface,9,muted,dpi),new(x,y));
        if(Samples.Count==0){Label("Waiting for readings",4,20);return;}
        var end=Samples[^1].At;var start=end-TimeSpan.FromSeconds(60);int first=Samples.Count-1;
        while(first>0&&Samples[first-1].At>=start&&Samples.Count-first<=60)first--;
        double? Value(Snapshot sample){
            if(Metric==SparkMetric.MemoryUsage)return double.IsFinite(sample.UsedRamGb)&&double.IsFinite(sample.TotalRamGb)&&sample.TotalRamGb>0&&sample.UsedRamGb>=0&&sample.UsedRamGb<=sample.TotalRamGb?100*(sample.UsedRamGb/sample.TotalRamGb):null;
            foreach(var d in sample.Devices)if(d.Id==DeviceId&&d.State!=ReadingState.Stale&&d.State!=ReadingState.Paused){
                if(Metric==SparkMetric.Load)return d.State is ReadingState.Available or ReadingState.Unavailable?d.Load:null;
                if(d.State!=ReadingState.Available)return null;
                return Fahrenheit&&d.Temperature.HasValue?d.Temperature*1.8+32:d.Temperature;
            }return null;
        }
        double low=double.MaxValue,high=double.MinValue;
        for(int i=first;i<Samples.Count;i++){var v=Value(Samples[i]);if(v is double n&&double.IsFinite(n)){low=Math.Min(low,n);high=Math.Max(high,n);}}
        if(low==double.MaxValue){Label("Waiting for readings",4,20);return;}
        string unit=Metric==SparkMetric.Temperature?(Fahrenheit?"°F":"°C"):"%";
        if(Metric!=SparkMetric.Temperature){low=0;high=100;}
        else{
            // Round to readable bounds; keep at least a 20-degree range so tiny changes aren't exaggerated.
            double middle=(low+high)/2;
            low=Math.Min(Math.Floor((middle-10)/10)*10,Math.Floor(low/10)*10);
            high=Math.Max(low+20,Math.Ceiling(high/10)*10);
        }
        ScaleMinimum=low;ScaleMaximum=high;
        const double left=40,top=5;
        double right=ActualWidth-5,bottom=ActualHeight-17;
        var key=(ActualWidth,ActualHeight,low,high,unit,dpi,GlassMaterial.HighContrastActive);
        if(axes==null||axesKey!=key){
            axes=new DrawingGroup();
            using(var drawing=axes.Open()){
                var grid=new Pen(divider,.7);
                for(int i=0;i<=2;i++){double y=top+(bottom-top)*i/2;drawing.DrawLine(grid,new(left,y),new(right,y));}
                drawing.DrawLine(grid,new(left,top),new(left,bottom));
                drawing.DrawLine(grid,new(right,top),new(right,bottom));
                Label($"{high:0} {unit}",0,top-4,drawing);Label($"{low:0} {unit}",0,bottom-7,drawing);
                Label("−60s",left,bottom+4,drawing);Label("Now",right-20,bottom+4,drawing);
            }
            axes.Freeze();axesKey=key;
        }
        dc.DrawDrawing(axes);
        var geometry=new StreamGeometry();Point? lastPoint=null;DateTimeOffset? previous=null;
        dc.PushClip(new RectangleGeometry(new Rect(left-3,top-3,right-left+6,bottom-top+6)));
        using(var path=geometry.Open()){
            for(int i=first;i<Samples.Count;i++){
                var sample=Samples[i];var v=Value(sample);
                if(v is not double value||!double.IsFinite(value)){previous=null;continue;}
                var point=new Point(left+(right-left)*Math.Clamp((sample.At-start).TotalSeconds/60,0,1),top+(bottom-top)*(1-(value-low)/(high-low)));
                if(previous.HasValue&&(sample.At-previous.Value).TotalSeconds<=16)path.LineTo(point,true,false);else path.BeginFigure(point,false,false);
                previous=sample.At;lastPoint=point;PlottedSamples++;
            }
        }
        geometry.Freeze();var pen=new Pen(ink,1.6){LineJoin=PenLineJoin.Round};
        dc.DrawGeometry(null,pen,geometry);
        if(lastPoint.HasValue&&Value(Samples[^1]).HasValue)dc.DrawEllipse(ink,null,lastPoint.Value,2.4,2.4);
        dc.Pop();
        if((end-Samples[first].At).TotalSeconds<15)Label("Collecting…",left+8,top+14);
    }
    private sealed class SparkPeer(MetricSparkline chart):FrameworkElementAutomationPeer(chart){
        protected override string GetNameCore()=>System.Windows.Automation.AutomationProperties.GetName((MetricSparkline)Owner);
        protected override string GetHelpTextCore()=>"Last 60 seconds of real sampled readings, with labeled scale and units. Empty time is unrecorded; gaps mean unavailable readings.";
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Image;
    }
}
