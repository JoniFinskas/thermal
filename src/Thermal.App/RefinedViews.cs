using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Thermal.Core;
namespace Thermal.App;

public partial class MainWindow {
    private TextBlock Text(string value,double size=14,FontWeight? weight=null,string ink="Ink") {
        var text=new TextBlock{Text=value,FontSize=size,FontWeight=weight??FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};
        text.SetResourceReference(TextBlock.ForegroundProperty,ink);return text;
    }
    private TextBlock Live(Func<string> value,double size=14,FontWeight? weight=null,string ink="Ink"){
        var text=Text(value(),size,weight,ink);refresh.Add(()=>{string next=value();if(text.Text!=next)text.Text=next;});return text;
    }
    private TextBlock RoleText(string value,string role,FontWeight? weight=null,string ink="Ink"){
        var text=Text(value,(double)FindResource("Type."+role),weight,ink);text.Tag=role;
        if(role=="PageTitle")System.Windows.Automation.AutomationProperties.SetHeadingLevel(text,System.Windows.Automation.AutomationHeadingLevel.Level1);
        return text;
    }
    private Grid PageHeader(string title,UIElement? action=null,string? subtitle=null){
        var row=Columns(Star,GridLength.Auto);row.Margin=new(0,3,0,16);row.MinHeight=40;
        var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Top};labels.Children.Add(RoleText(title,"PageTitle",FontWeights.SemiBold));
        if(subtitle!=null){var note=RoleText(subtitle,"Caption",ink:"Muted");note.TextWrapping=TextWrapping.Wrap;note.Margin=new(0,5,0,0);labels.Children.Add(note);}
        row.Children.Add(labels);if(action!=null){if(action is FrameworkElement control)control.VerticalAlignment=VerticalAlignment.Top;Place(row,action,1);}
        return row;
    }
    private Border Card(UIElement child,Thickness? padding=null){var card=new Border{Style=(Style)FindResource("Card"),Child=child};if(padding.HasValue)card.Padding=padding.Value;return card;}
    private Border Divider()=>new(){Height=1,Background=(Brush)FindResource("Divider")};
    private StackPanel ButtonContent(string icon,string label){var panel=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(new PhosphorIcon{Kind=icon,Width=16,Height=16,Margin=new(0,0,8,0)});panel.Children.Add(Text(label));return panel;}
    private Button Button(string label,Action action,string? icon=null){var b=new Button{Content=icon==null?Text(label):ButtonContent(icon,label)};b.Click+=(_,_)=>action();System.Windows.Automation.AutomationProperties.SetName(b,label);return b;}
    private Button Link(string label,Action action){var b=Button(label,action);b.Style=(Style)FindResource("Link");b.Content=Text(label,12,ink:"Accent");return b;}
    private Grid Columns(params GridLength[] widths){var grid=new Grid();foreach(var w in widths)grid.ColumnDefinitions.Add(new(){Width=w});return grid;}
    private static GridLength Px(double n)=>new(n);
    private static GridLength Star=>new(1,GridUnitType.Star);
    private static void Place(Grid grid,UIElement child,int column,int span=1){Grid.SetColumn(child,column);Grid.SetColumnSpan(child,span);grid.Children.Add(child);}
    private void RowLabel(Grid grid,string icon,string label,string? tooltip=null,int column=0){
        var panel=new Grid{Margin=new(0,0,9,0)};panel.ColumnDefinitions.Add(new(){Width=Px(32)});panel.ColumnDefinitions.Add(new(){Width=Star});
        panel.Children.Add(new PhosphorIcon{Kind=icon,Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});
        var name=Text(label);name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=tooltip??label;Place(panel,name,1);Place(grid,panel,column);
    }
    private string Temp(double? value)=>value.HasValue?$"{(fahrenheit?value*1.8+32:value):F0} °{(fahrenheit?"F":"C")}":"—";
    private string Percent(double? value)=>value.HasValue?$"{value:F0}%":"—";
    private Reading? Current(string id)=>latest?.Devices.FirstOrDefault(d=>d.Id==id);
    private string DeviceLabel(Reading d,bool package=true){
        if(d.Id=="cpu")return "CPU";
        if(d.Id.StartsWith("nvidia-",StringComparison.Ordinal))return "NVIDIA GPU"+(latest?.Devices.Count(x=>x.Id.StartsWith("nvidia-",StringComparison.Ordinal))>1?" "+(Array.IndexOf(latest!.Devices.Where(x=>x.Id.StartsWith("nvidia-",StringComparison.Ordinal)).Select(x=>x.Id).ToArray(),d.Id)+1):"");
        if(d.Name.Contains("Intel",StringComparison.OrdinalIgnoreCase))return "Intel Graphics";
        return d.Name;
    }
    private string DeviceIcon(Reading d)=>(d.Id=="cpu"||d.Id.StartsWith("cpu.package.",StringComparison.Ordinal))?"Cpu":d.Id.StartsWith("display-",StringComparison.Ordinal)?"Display":"Gpu";
    private (double? Min,double? Max) Range(string id,bool load=false){
        return history.Range(id,load);
    }
    private StackPanel Legend(){
        var panel=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var d in latest?.Devices.Where(d=>d.Temperature.HasValue&&d.State==ReadingState.Available).Take(3)??Array.Empty<Reading>()){
            string id=d.Id;var item=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(9,0,0,0)};
            item.Children.Add(new Border{Width=9,Height=3,CornerRadius=new(1),Background=new SolidColorBrush(TrendChart.TraceColor(id)),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,5,0)});
            var value=Live(()=>$"{DeviceLabel(d,false)} ({Temp(Current(id)?.Temperature)})",10);value.MaxWidth=150;value.TextTrimming=TextTrimming.CharacterEllipsis;item.Children.Add(value);panel.Children.Add(item);
        }return panel;
    }
    private TrendChart Chart(int duration,double height){var c=new TrendChart{Samples=history.Samples,Minutes=duration,Height=height,Margin=new(0,10,0,0),Fahrenheit=fahrenheit,Metric=historyMetric};charts.Add(c);return c;}
    private void RenderHistory(StackPanel stack){
        var actions=new StackPanel{Orientation=Orientation.Horizontal};var reset=Button("Reset",()=>{history.Clear();Render();},"Reset");reset.Margin=new(0,0,8,0);actions.Children.Add(reset);actions.Children.Add(ExportButton());stack.Children.Add(PageHeader("History",actions));
        var saved=Live(()=>exportFeedback,12,ink:"Muted");saved.TextWrapping=TextWrapping.Wrap;saved.Margin=new(0,0,0,8);saved.Visibility=exportFeedback.Length>0?Visibility.Visible:Visibility.Collapsed;stack.Children.Add(saved);
        var panel=new StackPanel();var controls=Columns(Star,GridLength.Auto);
        var metrics=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var metric in new[]{SparkMetric.Temperature,SparkMetric.Load,SparkMetric.MemoryUsage}){
            var selected=metric;string name=metric==SparkMetric.Temperature?"Temperature":metric==SparkMetric.Load?"Load":"Memory";
            var button=Button(name,()=>{historyMetric=selected;layoutKey="";Render();});button.Style=(Style)FindResource("Selection");button.Margin=new(0,0,4,0);
            if(historyMetric==metric)button.Tag="Selected";metrics.Children.Add(button);
        }
        controls.Children.Add(metrics);var periods=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(int value in new[]{1,5,15,60,240}){int duration=value;var button=Button(value>=60?$"{value/60}h":$"{value}m",()=>{minutes=duration;Render();});button.Style=(Style)FindResource("Selection");button.Padding=new(9,6,9,6);button.Margin=new(3,0,0,0);if(minutes==value)button.Tag="Selected";periods.Children.Add(button);}
        if(ActualWidth<740){controls.Children.Remove(metrics);panel.Children.Add(metrics);periods.Margin=new(0,10,0,0);panel.Children.Add(periods);}else{Place(controls,periods,1);panel.Children.Add(controls);}panel.Children.Add(Chart(minutes,200));
        stack.Children.Add(Card(panel,new Thickness(15)));
        var table=new StackPanel();var title=RoleText("Retained history","SectionTitle",FontWeights.SemiBold);title.Margin=new(0,0,0,5);table.Children.Add(title);
        title.ToolTip="Minimum and maximum cover the retained session.";
        var columns=Columns(Star,Px(96),Px(96),Px(96));string[] names=["Device","Current","Minimum","Maximum"];
        for(int i=0;i<4;i++)Place(columns,Text(names[i],12,ink:"Muted"),i);table.Children.Add(columns);table.Children.Add(Divider());
        void Add(string id,string name,Func<double?> current,bool load){
            var row=Columns(Star,Px(96),Px(96),Px(96));row.MinHeight=36;
            var label=Text(name,13);label.TextTrimming=TextTrimming.CharacterEllipsis;label.ToolTip=name;Place(row,label,0);
            string Format(double? value)=>load?Percent(value):Temp(value);
            Place(row,Live(()=>Format(current()),14,FontWeights.SemiBold),1);Place(row,Live(()=>Format(Range(id,load).Min),12),2);Place(row,Live(()=>Format(Range(id,load).Max),12),3);table.Children.Add(row);table.Children.Add(Divider());
        }
        if(historyMetric==SparkMetric.MemoryUsage)Add("ram","Physical RAM",()=>latest==null||latest.TotalRamGb<=0?null:100*latest.UsedRamGb/latest.TotalRamGb,true);
        else foreach(var device in latest?.Devices.Where(d=>historyMetric==SparkMetric.Load?d.Load.HasValue||Range(d.Id,true).Min.HasValue:d.Temperature.HasValue||Range(d.Id).Min.HasValue)??Array.Empty<Reading>()){
            string id=device.Id;Add(id,DeviceLabel(device,false),()=>historyMetric==SparkMetric.Load?Current(id)?.Load:Current(id)?.Temperature,historyMetric==SparkMetric.Load);
        }
        if(historyMetric==SparkMetric.Temperature&&latest?.Devices.Any(d=>d.Id=="cpu"&&!d.Temperature.HasValue)==true){var missing=Text("CPU temperature is unavailable. CPU load is available in Load.",11,ink:"Muted");missing.Margin=new(0,11,0,0);missing.TextWrapping=TextWrapping.Wrap;table.Children.Add(missing);}
        var data=Card(table,new Thickness(15));data.Margin=new(0,12,0,0);stack.Children.Add(data);
    }
    private void RenderSensors(StackPanel stack){
        stack.Children.Add(PageHeader("Sensors"));
        var rows=new StackPanel();var header=Columns(Star,Px(87),Px(89),Px(89));string[] titles=["Sensor","Current","Minimum","Maximum"];
        for(int i=0;i<4;i++)Place(header,Text(titles[i],12,ink:"Muted"),i);header.Margin=new(0,0,0,7);rows.Children.Add(header);
        foreach(var d in latest?.Devices??Array.Empty<Reading>()){
            var group=new Border{Background=(Brush)FindResource("GroupHeading"),BorderBrush=(Brush)FindResource("Divider"),BorderThickness=new(0,1,0,1),Padding=new(0,6,0,6)};
            var title=Columns(Px(22),Px(30),Star,GridLength.Auto);title.Children.Add(new PhosphorIcon{Kind="Chevron",Width=14,Height=14,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});Place(title,new PhosphorIcon{Kind=DeviceIcon(d),Width=17,Height=17,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center},1);var label=Text(DeviceLabel(d,false),14,FontWeights.SemiBold);label.TextTrimming=TextTrimming.CharacterEllipsis;label.ToolTip=d.Name;Place(title,label,2);bool isCollapsed=collapsed.TryGetValue(d.Id,out bool hidden)&&hidden;
            ((PhosphorIcon)title.Children[0]).RenderTransform=new RotateTransform(isCollapsed?-90:0,7,7);
            if(d.State!=ReadingState.Available){var state=Text(d.State==ReadingState.Unavailable&&d.Load.HasValue?"Load available":d.State.ToString(),11,ink:"Muted");state.Margin=new(8,0,0,0);Place(title,state,3);}
            var toggle=Button("Toggle "+DeviceLabel(d,false),()=>{collapsed[d.Id]=!isCollapsed;layoutKey="";Render();});
            System.Windows.Automation.AutomationProperties.SetHelpText(toggle,isCollapsed?"Collapsed. Activate to expand sensors.":"Expanded. Activate to collapse sensors.");
            toggle.Content=title;toggle.HorizontalContentAlignment=HorizontalAlignment.Stretch;toggle.Padding=new(0);toggle.Background=Brushes.Transparent;toggle.BorderBrush=Brushes.Transparent;
            group.Child=toggle;rows.Children.Add(group);
            if(!isCollapsed){AddSensorRow(rows,d,"Temperature",false);if(d.Load.HasValue||d.Id=="cpu")AddSensorRow(rows,d,"Load",true);}
        }
        var ramGroup=new Border{Background=(Brush)FindResource("GroupHeading"),BorderBrush=(Brush)FindResource("Divider"),BorderThickness=new(0,1,0,1),Padding=new(0,7,0,7)};var ramTitle=new StackPanel{Orientation=Orientation.Horizontal};ramTitle.Children.Add(new PhosphorIcon{Kind="Memory",Width=20,Height=20,Margin=new(22,0,10,0)});ramTitle.Children.Add(Text("Physical RAM",12,FontWeights.SemiBold));ramGroup.Child=ramTitle;rows.Children.Add(ramGroup);
        var ramRow=Columns(Star,Px(265));ramRow.MinHeight=33;Place(ramRow,Text("Used / total",12),0);Place(ramRow,Live(()=>latest==null?"—":$"{latest.UsedRamGb:F1} / {latest.TotalRamGb:F1} GiB"),1);ramRow.Margin=new(52,0,0,0);rows.Children.Add(ramRow);
        stack.Children.Add(Card(rows,new Thickness(11,10,11,8)));
        AcerSensors(stack);
        if(latest?.Devices.FirstOrDefault(d=>d.Id=="cpu")?.Temperature==null)stack.Children.Add(CpuNotice());
    }
    private void AddSensorRow(StackPanel rows,Reading d,string label,bool load){
        string id=d.Id;var row=Columns(Star,Px(87),Px(89),Px(89));row.MinHeight=31;
        var name=Text(label,12);name.Margin=new(52,0,0,0);Place(row,name,0);
        var current=Live(()=>load?Percent(Current(id)?.Load):Temp(Current(id)?.Temperature),14,FontWeights.SemiBold);
        System.Windows.Automation.AutomationProperties.SetAutomationId(current,id+(load?".load":".temperature"));Place(row,current,1);
        Place(row,Live(()=>load?Percent(Range(id,true).Min):Temp(Range(id).Min),12),2);
        var maximum=Live(()=>load?Percent(Range(id,true).Max):Temp(Range(id).Max),12);maximum.ToolTip=d.Detail;Place(row,maximum,3);row.ToolTip=d.Detail;rows.Children.Add(row);rows.Children.Add(Divider());
    }
    private Border CpuNotice(){
        var grid=Columns(Px(37),Star,GridLength.Auto);grid.Children.Add(new PhosphorIcon{Kind="Warning",Ink="WarningInk",Width=25,Height=25,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});
        var body=new StackPanel();body.Children.Add(Text("CPU temperature unavailable",12,FontWeights.SemiBold));var detail=Text(acerModel?(LocalAcerReaderHost.Enabled?"Allow CPU readings in Settings.":"CPU readings are not enabled."):"CPU temperature is supported only on qualified Acer systems. CPU load and RAM remain available.",12,ink:"Muted");detail.Margin=new(0,3,0,0);body.Children.Add(detail);Place(grid,body,1);
        Place(grid,Link("Access options",()=>Navigate("First run")),2);var card=Card(grid);card.Margin=new(0,11,0,0);return card;
    }
    private void RenderSettings(StackPanel stack){
        var version=Text("Version "+AppVersion.Current,12,ink:"Muted");version.HorizontalAlignment=HorizontalAlignment.Right;System.Windows.Automation.AutomationProperties.SetAutomationId(version,"AppVersion");
        stack.Children.Add(PageHeader("Settings",version));var panel=new StackPanel();
        var units=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(bool f in new[]{false,true}){bool selected=f;var b=Button(f?"°F":"°C",()=>{fahrenheit=selected;SavePreferences();layoutKey="";Render();});b.Style=(Style)FindResource("Selection");b.Padding=new(18,6,18,6);if(fahrenheit==f)b.Tag="Selected";units.Children.Add(b);}
        PreferenceRow(panel,"Units","Temperature unit",units);
        var combo=new ComboBox{Width=172};System.Windows.Automation.AutomationProperties.SetName(combo,"Sampling interval");foreach(int n in new[]{1,2,5})combo.Items.Add(n==1?"1 second":$"{n} seconds");combo.SelectedIndex=interval==1?0:interval==5?2:1;combo.SelectionChanged+=(_,_)=>{interval=combo.SelectedIndex==0?1:combo.SelectedIndex==2?5:2;SavePreferences();};
        PreferenceRow(panel,"Clock","Sampling interval",combo);
        var glass=new CheckBox{Style=(Style)FindResource("Toggle"),IsChecked=transparency};System.Windows.Automation.AutomationProperties.SetName(glass,"Glass window backdrop");glass.Click+=(_,_)=>{transparency=glass.IsChecked==true;ApplyBackdrop();SavePreferences();layoutKey="";Render();};
        PreferenceRow(panel,"Glass","Glass backdrop",glass);
        var compactToggle=new CheckBox{Style=(Style)FindResource("Toggle"),IsChecked=compact};System.Windows.Automation.AutomationProperties.SetName(compactToggle,"Compact mode");compactToggle.Click+=(_,_)=>SetCompact(compactToggle.IsChecked==true);
        PreferenceRow(panel,"Compact","Compact mode",compactToggle);
        var retention=new ComboBox{Width=172};retention.Items.Add("1 hour");retention.Items.Add("4 hours");retention.SelectedIndex=history.Retention.TotalHours==1?0:1;
        System.Windows.Automation.AutomationProperties.SetName(retention,"Session history duration");
        retention.SelectionChanged+=(_,_)=>{history.SetRetention(TimeSpan.FromHours(retention.SelectedIndex==0?1:4));SavePreferences();};
        PreferenceRow(panel,"History","Session history",retention,"Stored in memory. Closing the app clears it; a shorter duration drops older samples.");
        PreferenceRow(panel,"Reset","Clear session history",Button("Reset",()=>{history.Clear();Render();}),"Reset the graph and recorded minimum / maximum.");
        AcerPreferences(panel);
        LoggingPreferences(panel);
        var licences=Link("Licences",()=>Navigate("Licences"));licences.HorizontalAlignment=HorizontalAlignment.Right;licences.Margin=new(0,10,0,3);panel.Children.Add(licences);stack.Children.Add(Card(panel,new Thickness(16,8,16,8)));
    }
    private void PreferenceRow(StackPanel panel,string icon,string label,FrameworkElement control,string? detail=null){
        panel.Children.Add(Divider());var row=Columns(Px(33),Star,GridLength.Auto);row.MinHeight=detail==null?44:56;row.Margin=new(0,4,0,4);row.Children.Add(new PhosphorIcon{Kind=icon,Width=21,Height=21,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});
        var name=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,9,0)};name.Children.Add(Text(label,14));if(detail!=null){var caption=Text(detail,12,ink:"Muted");caption.TextWrapping=TextWrapping.Wrap;caption.Margin=new(0,4,0,0);name.Children.Add(caption);}Place(row,name,1);control.VerticalAlignment=VerticalAlignment.Center;Place(row,control,2);panel.Children.Add(row);
    }
    private void RenderLicences(StackPanel stack){
        stack.Children.Add(PageHeader("Licences",Button("Back to Settings",()=>Navigate("Settings")),"Original app: MIT · Phosphor Icons: MIT"));
        var content=new StackPanel();content.Children.Add(RoleText("Phosphor Icons 2.1.10 · regular weight","SectionTitle",FontWeights.SemiBold));
        var description=RoleText("This app includes adapted Phosphor vector shapes. The full upstream copyright and permission notice follows and is shipped in THIRD-PARTY-NOTICES.","Body");description.TextWrapping=TextWrapping.Wrap;description.LineHeight=21;description.Margin=new(0,10,0,16);content.Children.Add(description);
        using var stream=typeof(PhosphorIcon).Assembly.GetManifestResourceStream("Thermal.Phosphor.License")??throw new InvalidOperationException("Phosphor licence missing");
        using var reader=new System.IO.StreamReader(stream);var license=RoleText(reader.ReadToEnd(),"Body");license.TextWrapping=TextWrapping.Wrap;license.LineHeight=21;content.Children.Add(license);
        stack.Children.Add(Card(content,new Thickness(16)));
    }
    private void RenderFirstRun(StackPanel stack){
        stack.Children.Add(PageHeader("Available readings"));
        var capabilities=new StackPanel();Capability(capabilities,"Cpu","CPU load & RAM",latest?.CpuLoad.HasValue==true,"Ready");Capability(capabilities,"Gpu","GPU readings",latest?.Devices.Any(d=>(d.Temperature.HasValue||d.Load.HasValue)&&d.State!=ReadingState.Stale&&d.Id!="cpu"&&!d.Id.StartsWith("cpu.package.",StringComparison.Ordinal))==true,"Available");Capability(capabilities,"Thermometer","CPU temperature",latest?.Devices.Any(d=>d.Id=="cpu"&&d.Temperature.HasValue)==true,latest?.Devices.Any(d=>d.Id=="cpu"&&d.Temperature.HasValue)==true?"Available":"Not enabled");stack.Children.Add(Card(capabilities));
        stack.Children.Add(new Border{Height=22});stack.Children.Add(RoleText("CPU temperature","SectionTitle",FontWeights.SemiBold));var explanation=Text(latest?.Devices.Any(d=>d.Id=="cpu"&&d.Source=="acer-firmware"&&d.Temperature.HasValue)==true?"CPU temperature is available.":acerModel?(LocalAcerReaderHost.Enabled?(BuildPolicy.RequestAcerOnOpen?"Allow CPU readings when Windows asks, or enable them in Settings.":"Enable optional CPU readings in Settings, then approve the Windows permission prompt."):AcerAdmission.Pending):"CPU temperature requires a qualified Acer system. CPU load, RAM and supported GPU readings work independently.",12,ink:"Muted");explanation.TextWrapping=TextWrapping.Wrap;explanation.LineHeight=19;explanation.Margin=new(0,7,0,0);stack.Children.Add(explanation);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,22,0,0)};var continueButton=Button("Continue with available readings",()=>Navigate("Overview"));continueButton.Background=RedButton();continueButton.BorderBrush=(Brush)FindResource("Accent");actions.Children.Add(continueButton);var sensors=Button("View sensors",()=>Navigate("Sensors"));sensors.Margin=new(8,0,0,0);actions.Children.Add(sensors);stack.Children.Add(actions);
    }
    private void Capability(StackPanel panel,string icon,string label,bool ready,string success){var row=Columns(Px(34),Star,Px(32),Px(115));row.MinHeight=39;row.Children.Add(new PhosphorIcon{Kind=icon,Width=22,Height=22,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});Place(row,Text(label,13),1);Place(row,new PhosphorIcon{Kind=ready?"Check":"Warning",Ink=ready?"SuccessInk":"WarningInk",Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center},2);Place(row,Text(ready?success:label=="CPU temperature"?success:"Unavailable",11),3);panel.Children.Add(row);}
    private static Brush RedButton()=>GlassMaterial.HighContrastActive?SystemColors.WindowBrush:new SolidColorBrush(Color.FromRgb(211,48,61));
}
