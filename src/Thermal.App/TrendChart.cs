using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Thermal.Core;
namespace Thermal.App;
public sealed class TrendChart:FrameworkElement {
    public IReadOnlyList<Snapshot> Samples{get;set;}=Array.Empty<Snapshot>();
    public int Minutes{get;set;}=1;
    public bool Fahrenheit{get;set;}
    public SparkMetric Metric{get;set;}
    private Point? cursor;
    private DrawingGroup? graph,axes;
    private (double Width,double Height,double Dpi,double Minimum,double Maximum,string Unit,string Labels,bool Contrast) axesKey;
    private static readonly Typeface ChartTypeface=new("Segoe UI");
    private IReadOnlyList<Snapshot>? source;
    private (double Width,double Height,double Dpi,int Minutes,bool Fahrenheit,SparkMetric Metric,bool Contrast) key;
    private IReadOnlyList<Snapshot> selected=Array.Empty<Snapshot>();
    private string[] ids=Array.Empty<string>();
    private DateTimeOffset start;
    private double minimum,maximum;
    private const double Left=43,RightInset=12,Top=24;
    private static readonly Color[] colors=[Color.FromRgb(206,221,232),Color.FromRgb(244,191,110),Color.FromRgb(123,215,201),Color.FromRgb(208,171,229),Color.FromRgb(132,190,238)];
    public DateTimeOffset? InspectedTimestamp{get;private set;}
    public int InspectedValueCount{get;private set;}
    public int RenderedPointCount{get;private set;}
    internal double RenderedMinimum{get;private set;}
    internal double RenderedMaximum{get;private set;}
    public TrendChart(){Focusable=true;Cursor=Cursors.Cross;}
    public static Color TraceColor(string id){
        if(GlassMaterial.HighContrastActive)return id=="cpu"?SystemColors.HighlightColor:SystemColors.WindowTextColor;
        if(id=="cpu")return Color.FromRgb(255,74,81);
        if(id=="ram")return Color.FromRgb(255,74,81);
        int index=0;if(id.StartsWith("nvidia-",StringComparison.Ordinal))int.TryParse(id.AsSpan(7),out index);else foreach(char c in id)index=(index*31+c)%colors.Length;
        return colors[Math.Abs(index)%colors.Length];
    }
    private double? Value(Snapshot sample,string id){
        if(Metric==SparkMetric.MemoryUsage){
            if(!double.IsFinite(sample.UsedRamGb)||!double.IsFinite(sample.TotalRamGb)||sample.TotalRamGb<=0||sample.UsedRamGb<0||sample.UsedRamGb>sample.TotalRamGb)return null;
            return 100*(sample.UsedRamGb/sample.TotalRamGb);
        }
        for(int i=0;i<sample.Devices.Count;i++)if(sample.Devices[i].Id==id)return ReadingValue(sample.Devices[i]);
        return null;
    }
    private double? ReadingValue(Reading reading){
        if(reading.State is ReadingState.Stale or ReadingState.Paused or ReadingState.Unsupported or ReadingState.AccessDenied)return null;
        double? value=Metric==SparkMetric.Load?reading.Load:reading.State==ReadingState.Available?reading.Temperature:null;
        if(value is not double number||!double.IsFinite(number))return null;
        if(Metric==SparkMetric.Temperature&&Fahrenheit)number=number*1.8+32;
        return double.IsFinite(number)?number:null;
    }
    // History supplies immutable snapshots: a narrow window needs an indexed view, not another full array copy.
    private sealed class SampleWindow(IReadOnlyList<Snapshot> source,int offset):IReadOnlyList<Snapshot>{
        public int Count=>source.Count-offset;
        public Snapshot this[int index]=>index>=0&&index<Count?source[offset+index]:throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<Snapshot> GetEnumerator(){for(int i=offset;i<source.Count;i++)yield return source[i];}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private string Unit=>Metric==SparkMetric.Temperature?(Fahrenheit?"°F":"°C"):"%";
    private string SeriesName(string id)=>id=="cpu"?"CPU":id=="ram"?"Physical RAM":id.StartsWith("nvidia-",StringComparison.Ordinal)?"GPU "+(int.TryParse(id.AsSpan(7),out int n)?n+1:1):id;
    protected override AutomationPeer OnCreateAutomationPeer()=>new ChartPeer(this);
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var point=e.GetPosition(this);if(cursor.HasValue&&point.Y>=Top&&point.Y<=ActualHeight-29&&Math.Abs(point.X-cursor.Value.X)<1)return;InspectPointerAt(point);}
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();InspectPointerAt(e.GetPosition(this));e.Handled=true;}
    protected override void OnMouseLeave(MouseEventArgs e){base.OnMouseLeave(e);cursor=null;InvalidateVisual();}
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e){base.OnLostKeyboardFocus(e);cursor=null;InvalidateVisual();}
    protected override void OnKeyDown(KeyEventArgs e){
        base.OnKeyDown(e);if(e.Key is not(Key.Left or Key.Right)||selected.Count==0)return;
        int index=selected.Count-1;
        if(InspectedTimestamp.HasValue){int lo=0,hi=selected.Count-1;while(lo<hi){int mid=(lo+hi)/2;if(selected[mid].At<InspectedTimestamp.Value)lo=mid+1;else hi=mid;}index=lo;}
        index=Math.Clamp(index+(e.Key==Key.Right?1:-1),0,selected.Count-1);
        cursor=new(X(selected[index].At),Top);InvalidateVisual();e.Handled=true;
    }
    public void InspectPointerAt(Point point){
        cursor=point.X>=Left&&point.X<=ActualWidth-RightInset&&point.Y>=Top&&point.Y<=ActualHeight-29?point:null;
        InvalidateVisual();
    }
    public void InspectAt(double fraction){cursor=new(Left+(ActualWidth-Left-RightInset)*Math.Clamp(fraction,0,1),Top);InvalidateVisual();}
    private double X(DateTimeOffset at)=>Left+(ActualWidth-Left-RightInset)*(at-start).TotalSeconds/(Minutes*60);
    private double Y(double value)=>Top+(ActualHeight-29-Top)*(1-(value-minimum)/(maximum-minimum));
    private void Text(DrawingContext dc,string value,double x,double y,Brush brush,double size=11)=>dc.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,ChartTypeface,size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new(x,y));
    private void Build(DrawingContext dc){
        double right=ActualWidth-RightInset,bottom=ActualHeight-29;
        var muted=TryFindResource("Muted") as Brush??Brushes.LightGray;
        var grid=new Pen(TryFindResource("Divider") as Brush??Brushes.Gray,.6);
        var end=Samples.Count==0?DateTimeOffset.UtcNow:Samples[^1].At;start=end-TimeSpan.FromMinutes(Minutes);
        int firstIndex=0,lastIndex=Samples.Count;
        while(firstIndex<lastIndex){int mid=(firstIndex+lastIndex)/2;if(Samples[mid].At<start)firstIndex=mid+1;else lastIndex=mid;}
        selected=firstIndex==0?Samples:new SampleWindow(Samples,firstIndex);
        double rangeLow=double.MaxValue,rangeHigh=double.MinValue;
        if(Metric==SparkMetric.MemoryUsage)ids=selected.Count>0?["ram"]:Array.Empty<string>();
        else{
            var found=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<selected.Count;i++){
                var devices=selected[i].Devices;
                for(int n=0;n<devices.Count;n++){
                    var reading=devices[n];if(ReadingValue(reading) is not double value)continue;
                    if(!seen.Contains(reading.Id)){if(found.Count>=8)continue;seen.Add(reading.Id);found.Add(reading.Id);}
                    if(Metric==SparkMetric.Temperature){rangeLow=Math.Min(rangeLow,value);rangeHigh=Math.Max(rangeHigh,value);}
                }
            }
            ids=found.ToArray();
        }
        RenderedPointCount=0;RenderedMinimum=double.PositiveInfinity;RenderedMaximum=double.NegativeInfinity;
        minimum=0;maximum=100;
        if(Metric==SparkMetric.Temperature&&rangeLow!=double.MaxValue){minimum=Math.Floor((rangeLow-5)/10)*10;maximum=Math.Max(minimum+20,Math.Ceiling((rangeHigh+5)/10)*10);}
        var labels=new string[7];
        for(int i=0;i<=6;i++)labels[i]=Minutes==1?(i==6?"Now":$"−{60-i*10}s"):i==6?"Now":start.AddMinutes(Minutes*i/6d).LocalDateTime.ToString("HH:mm");
        var nextAxes=(ActualWidth,ActualHeight,VisualTreeHelper.GetDpi(this).PixelsPerDip,minimum,maximum,Unit,string.Join("|",labels),GlassMaterial.HighContrastActive);
        if(axes==null||axesKey!=nextAxes){
            axes=new DrawingGroup();using(var axis=axes.Open()){
                Text(axis,Unit,2,1,muted);
                for(int i=0;i<=4;i++){double value=minimum+(maximum-minimum)*i/4,y=Y(value);axis.DrawLine(grid,new(Left,y),new(right,y));Text(axis,$"{value:0}",2,y-7,muted,10);}
                for(int i=0;i<=6;i++){double x=Left+(right-Left)*i/6;axis.DrawLine(grid,new(x,Top),new(x,bottom));Text(axis,labels[i],x-(i==0?0:i==6?23:14),bottom+9,muted,10);}
            }
            axes.Freeze();axesKey=nextAxes;
        }
        dc.DrawDrawing(axes);
        dc.PushClip(new RectangleGeometry(new Rect(Left,Top,right-Left,bottom-Top)));
        double xScale=(right-Left)/(TimeSpan.TicksPerSecond*60d*Minutes),yScale=(bottom-Top)/(maximum-minimum),plotHeight=bottom-Top;
        long startTicks=start.UtcTicks;
        foreach(string id in ids){
            var pen=new Pen(new SolidColorBrush(TraceColor(id)),1.8){LineJoin=PenLineJoin.Round};pen.Freeze();
            var path=new StreamGeometry();Point? lastPoint=null;DateTimeOffset? previous=null;
            using(var points=path.Open()){
                // Preserve first, last and both extrema in each physical-pixel time bucket.
                // Exact retained samples remain the source for cursor inspection.
                bool active=false,connected=false;int bucket=-1;
                Point first=default,low=default,high=default,last=default;
                double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;Point? emitted=null;
                void Emit(Point point){
                    if(emitted==point)return;
                    if(connected)points.LineTo(point,true,false);else{points.BeginFigure(point,false,false);connected=true;}
                    emitted=point;RenderedPointCount++;
                    double value=minimum+(1-(point.Y-Top)/plotHeight)*(maximum-minimum);
                    RenderedMinimum=Math.Min(RenderedMinimum,value);RenderedMaximum=Math.Max(RenderedMaximum,value);
                }
                void Flush(){
                    if(!active)return;Emit(first);
                    if(low.X<=high.X){Emit(low);Emit(high);}else{Emit(high);Emit(low);}
                    Emit(last);active=false;
                }
                foreach(var sample in selected){
                    if(Value(sample,id) is not double value){Flush();previous=null;connected=false;emitted=null;continue;}
                    var point=new Point(Left+(sample.At.UtcTicks-startTicks)*xScale,bottom-(value-minimum)*yScale);int nextBucket=(int)Math.Floor(point.X*dpi);
                    if(previous.HasValue&&(sample.At-previous.Value).TotalSeconds>16){Flush();connected=false;emitted=null;}
                    if(active&&nextBucket!=bucket)Flush();
                    if(!active){first=low=high=last=point;bucket=nextBucket;active=true;}
                    else{last=point;if(point.Y<low.Y)low=point;if(point.Y>high.Y)high=point;}
                    previous=sample.At;lastPoint=point;
                }
                Flush();
            }
            path.Freeze();dc.DrawGeometry(null,pen,path);
            if(lastPoint.HasValue)dc.DrawEllipse(pen.Brush,null,lastPoint.Value,2.8,2.8);
        }
        dc.Pop();
        if(ids.Length==0)Text(dc,"No available readings for this metric",Left+20,Top+35,muted,13);
        else{
            var legend=new Point(Left,3);
            for(int i=0;i<ids.Length;i++){
                string id=ids[i],name=SeriesName(id);if(name.Length>16)name=name[..15]+"…";double width=40+name.Length*6;
                if(legend.X+width>right-48){Text(dc,$"+{ids.Length-i} more",legend.X,legend.Y-2,muted,10);break;}
                var brush=new SolidColorBrush(TraceColor(id));dc.DrawLine(new Pen(brush,2),legend,new(legend.X+11,legend.Y+5));Text(dc,name,legend.X+16,legend.Y-2,muted,10);legend.X+=width;
            }
        }
    }
    protected override void OnRender(DrawingContext dc){
        base.OnRender(dc);dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));InspectedTimestamp=null;InspectedValueCount=0;
        if(ActualWidth<100||ActualHeight<90)return;
        var next=(ActualWidth,ActualHeight,VisualTreeHelper.GetDpi(this).PixelsPerDip,Minutes,Fahrenheit,Metric,GlassMaterial.HighContrastActive);
        if(graph==null||!ReferenceEquals(source,Samples)||key!=next){graph=new DrawingGroup();using(var drawing=graph.Open())Build(drawing);graph.Freeze();source=Samples;key=next;}
        dc.DrawDrawing(graph);
        var ink=TryFindResource("Ink") as Brush??Brushes.White;
        if(IsKeyboardFocused)dc.DrawRoundedRectangle(null,new Pen(ink,1),new(0,0,ActualWidth,ActualHeight),4,4);
        if(!cursor.HasValue||selected.Count==0||ids.Length==0)return;
        double requested=Math.Clamp(cursor.Value.X,Left,ActualWidth-RightInset);
        var target=start+TimeSpan.FromSeconds((requested-Left)/(ActualWidth-Left-RightInset)*Minutes*60);
        // Binary search retains exact real samples without a full-history scan per mouse event.
        int lo=0,hi=selected.Count-1;while(lo<hi){int mid=(lo+hi)/2;if(selected[mid].At<target)lo=mid+1;else hi=mid;}
        int chosen=lo;if(lo>0&&Math.Abs((selected[lo-1].At-target).Ticks)<=Math.Abs((selected[lo].At-target).Ticks))chosen--;
        var sample=selected[chosen];if(Math.Abs((sample.At-target).TotalSeconds)>8)return;InspectedTimestamp=sample.At;
        var values=ids.Select(id=>(Id:id,Value:Value(sample,id))).Where(v=>v.Value.HasValue).ToArray();InspectedValueCount=values.Length;
        if(values.Length==0)return;
        double x=X(sample.At),right=ActualWidth-RightInset,bottom=ActualHeight-29;
        dc.DrawLine(new Pen(ink,.7){DashStyle=DashStyles.Dot},new(x,Top),new(x,bottom));
        double width=Math.Min(205,right-Left-12);
        double bx=Math.Clamp(x+12,Left+4,Math.Max(Left+4,right-width)),by=Top+9;
        double height=Math.Min(31+values.Length*23,ActualHeight-by-3),rowHeight=(height-31)/values.Length;
        dc.DrawRoundedRectangle(TryFindResource("MenuSurface") as Brush??Brushes.Black,new Pen(TryFindResource("Edge") as Brush??ink,1),new(bx,by,width,height),8,8);
        Text(dc,sample.At.LocalDateTime.ToString("HH:mm:ss"),bx+11,by+8,ink);
        for(int i=0;i<values.Length;i++){
            var value=values[i];var color=new SolidColorBrush(TraceColor(value.Id));double y=by+31+i*rowHeight;
            dc.DrawEllipse(color,null,new(bx+14,y+6),3,3);string name=SeriesName(value.Id);if(name.Length>15)name=name[..14]+"…";
            Text(dc,name,bx+24,y,ink);Text(dc,$"{value.Value:0} {Unit}",bx+width-60,y,ink);
            dc.DrawEllipse(color,new Pen(ink,.7),new(x,Y(value.Value!.Value)),3,3);
        }
    }
    private sealed class ChartPeer(TrendChart chart):FrameworkElementAutomationPeer(chart){
        protected override string GetNameCore()=>chart.Metric==SparkMetric.Temperature?"Temperature history":chart.Metric==SparkMetric.Load?"Load history":"Memory usage history";
        protected override string GetHelpTextCore()=>"Use left/right arrows to inspect adjacent real samples. Gaps and unrecorded time stay empty. Exact current and retained range values are in the table.";
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Image;
    }
}
