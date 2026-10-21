using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private void RenderOverview(StackPanel stack){
        var header=PageHeader("Overview");
        stack.Children.Add(header);
        var rows=new StackPanel();
        if(latest==null){rows.Children.Add(Text("Reading CPU load, memory and available GPU sensors…",14,ink:"Muted"));}
        foreach(var device in latest?.Devices??Array.Empty<Reading>()){
            if(device.Id!="cpu"&&device.State==ReadingState.Unsupported&&!device.Temperature.HasValue&&!device.Load.HasValue)continue;
            string id=device.Id;bool cpu=id=="cpu";
            var row=SignalRow(DeviceIcon(device),cpu?"CPU":DeviceLabel(device,false),device.Name,
                "Load",()=>Percent(Current(id)?.Load),
                "Temperature",()=>Temp(Current(id)?.Temperature),
                new MetricSparkline{DeviceId=id,Metric=device.Temperature.HasValue?SparkMetric.Temperature:SparkMetric.Load},
                (cpu?"CPU":"GPU")+(device.Temperature.HasValue?" temperature":" load")+" · last 60 seconds");
            rows.Children.Add(row);rows.Children.Add(Divider());
        }
        rows.Children.Add(SignalRow("Memory","Physical RAM","Usable physical memory",
            "Used / total",()=>latest==null?"—":$"{latest.UsedRamGb:F1} / {latest.TotalRamGb:F1} GiB",
            "Usage",()=>latest==null?"—":Percent(100*latest.UsedRamGb/latest.TotalRamGb),
            new MetricSparkline{Metric=SparkMetric.MemoryUsage},"Memory usage · last 60 seconds"));
        stack.Children.Add(Card(rows,new Thickness(18,3,18,3)));
        int missing=latest?.Devices.Count(d=>d.Id!="cpu"&&d.State==ReadingState.Unsupported&&!d.Temperature.HasValue&&!d.Load.HasValue)??0;
        if(missing>0){var unavailable=Link($"Unavailable sensors · {missing} adapter{(missing==1?"":"s")}",()=>Navigate("Sensors"));unavailable.HorizontalAlignment=HorizontalAlignment.Left;unavailable.Margin=new(0,7,0,0);stack.Children.Add(unavailable);}
    }
    private Grid SignalRow(string icon,string name,string model,string firstLabel,Func<string> first,string secondLabel,Func<string> second,MetricSparkline trend,string trendLabel){
        bool narrow=ActualWidth<740;
        var row=narrow?Columns(Star,Star,Px(178)):Columns(Star,Px(174),Px(132),Px(190));
        row.Margin=new(0,12,0,12);row.MinHeight=narrow?116:82;
        if(narrow){row.RowDefinitions.Add(new(){Height=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});}
        var identity=Columns(Px(47),Star);identity.Margin=new(0,0,12,narrow?13:0);
        identity.Children.Add(new Border{Width=35,Height=35,HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new(9),Background=(Brush)FindResource("GroupHeading"),VerticalAlignment=VerticalAlignment.Center,Child=new PhosphorIcon{Kind=icon,Width=24,Height=24,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center}});
        var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        var label=Text(name,16,FontWeights.SemiBold);label.TextTrimming=TextTrimming.CharacterEllipsis;label.ToolTip=name;labels.Children.Add(label);
        var detail=Text(model,12,ink:"Muted");detail.Margin=new(0,5,0,0);detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.ToolTip=model;labels.Children.Add(detail);
        Place(identity,labels,1);Place(row,identity,0,narrow?3:1);
        StackPanel Metric(string caption,Func<string> value){
            var panel=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0)};
            panel.Children.Add(Text(caption,12,ink:"Muted"));
            var text=MetricValue(value,caption=="Used / total"?21:28);text.Margin=new(0,4,0,0);panel.Children.Add(text);
            if(caption is "Temperature" or "Firmware temp."){
                var state=Live(()=>Current(trend.DeviceId)?.Temperature.HasValue==true?"":Current(trend.DeviceId)?.State==ReadingState.Stale?"Reading stale":trend.DeviceId=="cpu"?(latest?.Acer?.CpuTemperature.State==ReadingState.Stale?"Reading stale":acerEnabled?"Unavailable":"Not enabled"):"Unavailable",11,ink:"Muted");
                state.Margin=new(0,3,0,0);panel.Children.Add(state);
            }
            return panel;
        }
        var primary=Metric(firstLabel,first);var secondary=Metric(secondLabel,second);
        Place(row,primary,narrow?0:1);Place(row,secondary,narrow?1:2);
        trend.Samples=history.RecentSamples;trend.Height=64;trend.Fahrenheit=fahrenheit;
        System.Windows.Automation.AutomationProperties.SetName(trend,trendLabel);trend.ToolTip=trendLabel;sparklines.Add(trend);
        var plot=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,0,0)};plot.Children.Add(trend);

        var inspect=Button("Inspect "+trendLabel.Split('·')[0].Trim(),()=>{historyMetric=trend.Metric;minutes=1;Navigate("History");});
        inspect.Content=plot;inspect.Padding=new(7,3,7,3);inspect.Background=Brushes.Transparent;inspect.BorderBrush=Brushes.Transparent;inspect.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        System.Windows.Automation.AutomationProperties.SetHelpText(inspect,"Open detailed history for this reading.");
        Place(row,inspect,narrow?2:3);
        if(narrow){Grid.SetRow(primary,1);Grid.SetRow(secondary,1);Grid.SetRow(inspect,1);}
        return row;
    }
    private Grid MetricValue(Func<string> value,double size){
        var panel=Columns(Star,GridLength.Auto);panel.HorizontalAlignment=HorizontalAlignment.Left;panel.VerticalAlignment=VerticalAlignment.Center;panel.MaxWidth=size==21?166:124;
        var number=Text("",size,FontWeights.SemiBold);var unit=Text("",Math.Max(12,size*.55),ink:"Muted");
        unit.Margin=new(6,0,0,2);unit.VerticalAlignment=VerticalAlignment.Bottom;
        number.TextWrapping=TextWrapping.Wrap;panel.Children.Add(number);Place(panel,unit,1);string previous="";
        void Update(){
            string next=value();if(next==previous)return;previous=next;
            int split=next.EndsWith("%",StringComparison.Ordinal)?next.Length-1:next.LastIndexOf(' ');
            number.Text=split<0?next:next[..split];unit.Text=split<0?"":next[split..].Trim();
            unit.Visibility=unit.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
            number.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));unit.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            panel.Width=Math.Min(panel.MaxWidth,number.DesiredSize.Width+(unit.Visibility==Visibility.Visible?unit.DesiredSize.Width:0));
            System.Windows.Automation.AutomationProperties.SetName(panel,next);
        }
        Update();refresh.Add(Update);return panel;
    }
    private int CompactWindowHeight=>Math.Clamp(108+((latest?.Devices.Count(d=>d.Id=="cpu"||d.Temperature.HasValue||d.Load.HasValue)??2)+1)*58,230,620);
    private void RenderCompact(){
        var rows=new StackPanel();
        Grid Row()=>Columns(Px(31),Star,Px(82),Px(64));
        var heading=Row();heading.Margin=new(0,8,0,2);
        var temperature=Text("Temp",11,ink:"Muted");temperature.HorizontalAlignment=HorizontalAlignment.Right;temperature.Margin=new(0,0,8,0);Place(heading,temperature,2);
        var utilization=Text("Load",11,ink:"Muted");utilization.HorizontalAlignment=HorizontalAlignment.Right;Place(heading,utilization,3);rows.Children.Add(heading);
        foreach(var device in latest?.Devices.Where(d=>d.Id=="cpu"||d.Temperature.HasValue||d.Load.HasValue)??Array.Empty<Reading>()){
            string id=device.Id;var row=Row();row.MinHeight=56;
            row.Children.Add(new PhosphorIcon{Kind=DeviceIcon(device),Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});
            var label=Text(DeviceLabel(device,false),12,FontWeights.SemiBold);label.ToolTip=device.Name;label.TextTrimming=TextTrimming.CharacterEllipsis;label.Margin=new(0,0,8,0);Place(row,label,1);
            var temp=MetricValue(()=>Temp(Current(id)?.Temperature),20);temp.HorizontalAlignment=HorizontalAlignment.Right;temp.Margin=new(0,0,8,0);temp.ToolTip=device.Detail;Place(row,temp,2);
            var load=MetricValue(()=>Percent(Current(id)?.Load),20);load.HorizontalAlignment=HorizontalAlignment.Right;Place(row,load,3);
            System.Windows.Automation.AutomationProperties.SetAutomationId(temp,id+".compact.temperature");
            System.Windows.Automation.AutomationProperties.SetAutomationId(load,id+".compact.load");
            rows.Children.Add(row);rows.Children.Add(Divider());
        }
        var ram=Row();ram.MinHeight=56;ram.Children.Add(new PhosphorIcon{Kind="Memory",Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});
        Place(ram,Text("RAM",12,FontWeights.SemiBold),1);
        var memory=Live(()=>latest==null?"—":$"{latest.UsedRamGb:F1} / {latest.TotalRamGb:F1} GiB",14,FontWeights.SemiBold);memory.HorizontalAlignment=HorizontalAlignment.Right;memory.TextTrimming=TextTrimming.CharacterEllipsis;memory.ToolTip="Used / total RAM";Place(ram,memory,2,2);rows.Children.Add(ram);
        var card=Card(rows,new Thickness(12,0,12,0));card.VerticalAlignment=VerticalAlignment.Top;card.Margin=new(0,3,0,0);
        ContentHost.Children.Add(new ScrollViewer{Style=(Style)FindResource(typeof(ScrollViewer)),Content=card,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
    }
}
