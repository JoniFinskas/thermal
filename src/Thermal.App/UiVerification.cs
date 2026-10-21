using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Thermal.Core;
namespace Thermal.App;

public partial class MainWindow {
    private async Task VerifyUi(bool design){
        bool acerFixture=Environment.GetCommandLineArgs().Contains("--verify-acer");
        interval=1;bool warm=Environment.GetCommandLineArgs().Contains("--warm-history");if(warm)minutes=1;
        string directory=System.IO.Path.Combine(AppContext.BaseDirectory,acerFixture?"acer-verification":design?"design-verification":"ui-verification");
        Directory.CreateDirectory(directory);
        foreach(string name in new[]{"result.json","error.txt","steps.txt"}){string owned=System.IO.Path.Combine(directory,name);if(File.Exists(owned))File.Delete(owned);}
        Window? stage=null;
        void Step(string label)=>File.AppendAllText(System.IO.Path.Combine(directory,"steps.txt"),DateTimeOffset.UtcNow.ToString("O")+" "+label+"\n");
        Step("begin");if(loggingRequested||localLog.IsEnabled||Directory.Exists(localLog.DirectoryPath))throw new InvalidOperationException("Logging must default to off with no log directory");
        var watchdog=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(warm?140:90)};
        watchdog.Tick+=(_,_)=>{Step("UI watchdog expired");Environment.ExitCode=1;Application.Current.Shutdown(1);};watchdog.Start();
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler report=(_,e)=>{Step("Dispatcher exception: "+e.Exception);e.Handled=true;Environment.ExitCode=1;Application.Current.Shutdown(1);};
        Application.Current.DispatcherUnhandledException+=report;
        try{
            await Task.Delay(warm?65000:9000);
            if(latest==null||latest.TotalRamGb<=0||!latest.CpuLoad.HasValue)throw new InvalidOperationException("Live baseline metrics did not initialize");
            await VerifyReleaseSurface(directory);
            foreach(var fixture in new[]{("100%",28d),("244 °F",28d),("1024.0 / 2048.0 GiB",21d),("—",28d)}){
                var metric=MetricValue(()=>fixture.Item1,fixture.Item2);metric.Measure(new Size(166,200));metric.Arrange(new Rect(0,0,metric.DesiredSize.Width,metric.DesiredSize.Height));metric.UpdateLayout();
                var values=metric.Children.OfType<TextBlock>().ToArray();
                if(metric.ActualWidth>166||values.Any(t=>t.ActualHeight>metric.ActualHeight))throw new InvalidOperationException("Large metric fixture clipped");
                if(values[1].Visibility==Visibility.Visible&&values[1].TranslatePoint(new Point(),metric).X<values[0].ActualWidth+5)throw new InvalidOperationException("Large metric fixture overlaps units");
            }
            if(design){
                var at=DateTimeOffset.UtcNow;var samples=Enumerable.Range(0,14400).Select(i=>new Snapshot(at.AddSeconds(i-14400),0,i==7201?97:i==7202?2:50,100,Array.Empty<Reading>())).ToArray();
                var stress=new TrendChart{Samples=samples,Minutes=240,Metric=SparkMetric.MemoryUsage,Width=500,Height=200};
                stress.Measure(new Size(500,200));stress.Arrange(new Rect(0,0,500,200));CaptureElement(stress,System.IO.Path.Combine(directory,"explicit-four-hour-memory-fixture.png"));
                if(stress.RenderedPointCount>=samples.Length/2||Math.Abs(stress.RenderedMinimum-2)>.001||Math.Abs(stress.RenderedMaximum-97)>.001)throw new InvalidOperationException("Pixel-bucket rendering lost a recorded peak/trough or failed to reduce draw work");
            }
            var live=latest;verificationFreeze=true;WindowState=WindowState.Normal;Show();Render();
            PauseClick();if(!paused||!Status.Text.Contains("paused",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Pause feedback failed");
            var frozenAt=latest.At;int frozenCount=history.Samples.Count;await Task.Delay(150);
            if(latest.At!=frozenAt||history.Samples.Count!=frozenCount)throw new InvalidOperationException("Queued sample changed paused readings");
            PauseClick();if(paused)throw new InvalidOperationException("Resume failed");
            if(design){TitleLabel.Text="Thermal · design QA";}
            if(design){history.Clear();foreach(var sample in DesignSamples())history.Add(sample);latest=history.Samples[^1];}
            if(acerFixture){
                var original=history.Samples.ToArray();history.Clear();foreach(var sample in original){var firmware=new AcerSample(sample.At,new(65,ReadingState.Available),new(null,ReadingState.Unsupported),new(null,ReadingState.Unsupported),new(null,ReadingState.Unsupported),LocalAcerReaderHost.CpuOnlyDetail);history.Add(AcerTelemetryProtocol.Merge(sample,firmware));}latest=history.Samples[^1];
                if(acerReader!=null||acerEnabled||latest.Devices.First(d=>d.Id=="cpu").Source!="acer-firmware")throw new InvalidOperationException("Acer fixture unexpectedly launched a helper or lost its source");
                TitleLabel.Text="Thermal · explicit Acer fixture";
            }
            stage=CreateVerificationStage();stage.Show();Owner=stage;Activate();await Task.Delay(700);
            var captures=new List<string>();
            foreach(string name in new[]{"Overview","History","Sensors","Specs","Settings","First run"}){
                Navigate(name);await Task.Delay(350);
                if(name=="Specs"){
                    if(specsTask==null)throw new InvalidOperationException("Specs discovery did not start");
                    if(await Task.WhenAny(specsTask,Task.Delay(9000))!=specsTask)throw new InvalidOperationException("Specs discovery timed out in this check");
                    await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
                    if(specifications==null||!specifications.Any(s=>s.Title=="Processor")||!specifications.Any(s=>s.Title=="Graphics")||!specifications.Any(s=>s.Title=="Storage"))throw new InvalidOperationException("Hardware inventory categories missing");
                    if(specifications.Where(s=>s.Title=="Processor").SelectMany(s=>s.Rows).All(r=>r.Label=="Status"))throw new InvalidOperationException("CPU inventory was not supplied by Windows: "+string.Join("; ",specifications.Where(s=>s.Title=="Processor").SelectMany(s=>s.Rows).Select(r=>r.Value)));
                }
                if(name=="Overview"){
                    if(sparklines.Count<=3&&ActualWidth>=850&&VisualChildren(Root).OfType<ScrollViewer>().Single().ScrollableHeight>1)throw new InvalidOperationException("Standard overview unnecessarily scrolls");
                    var columns=VisualChildren(Root).OfType<Grid>().Where(g=>g.ColumnDefinitions.Count==4&&g.Children.OfType<Grid>().Any()).ToArray();
                    foreach(var row in columns){
                        string Caption(int column)=>((StackPanel)row.Children.Cast<UIElement>().First(x=>Grid.GetColumn(x)==column)).Children.OfType<TextBlock>().First().Text;
                        if(Caption(1)!="Used / total"&&(Caption(1)!="Load"||Caption(2)!="Temperature"))throw new InvalidOperationException("Device metric columns are inconsistent");
                    }
                    foreach(var chart in sparklines){
                        if(chart.PlottedSamples<1)throw new InvalidOperationException("Overview trend has no actual data");
                        if(chart.Metric!=SparkMetric.Temperature&&(chart.ScaleMinimum!=0||chart.ScaleMaximum!=100))throw new InvalidOperationException("Utilization trend must use a percentage scale");
                        if(chart.PlottedSamples>61)throw new InvalidOperationException("Trend sample window is not bounded");
                    }
                    foreach(var pair in VisualChildren(Root).OfType<Grid>().Where(g=>g.ColumnDefinitions.Count==2&&g.Children.OfType<TextBlock>().Count()==2)){
                        var texts=pair.Children.OfType<TextBlock>().ToArray();if(texts[1].Visibility!=Visibility.Visible)continue;
                        var point=texts[1].TranslatePoint(new Point(),pair);
                        if(point.X<texts[0].ActualWidth+5||point.X>texts[0].ActualWidth+8)throw new InvalidOperationException("Metric number/unit have insufficient separation");
                    }
                    OverviewTab.Focus();await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
                    var face=(Border)OverviewTab.Template.FindName("Face",OverviewTab);
                    if(face.BorderThickness.Left!=0)throw new InvalidOperationException("Mouse/click focus must not leave a selected-tab border");
                    if(OverviewTab.FocusVisualStyle==null)throw new InvalidOperationException("Keyboard navigation must keep a focus cue");
                    if(VisualChildren(Root).OfType<TextBlock>().Any(t=>t.Text=="Readings stay on this PC"))throw new InvalidOperationException("Removed footer text returned");
                    {
                    }
                }
                if(name=="History"){charts.Single().InspectAt(design?.52:.9999);await Task.Delay(50);
                    if(!charts.Single().InspectedTimestamp.HasValue||!history.Samples.Any(s=>s.At==charts.Single().InspectedTimestamp))throw new InvalidOperationException("History cursor is not tied to a retained sample");
                    if(charts.Single().InspectedValueCount==0)throw new InvalidOperationException("History tooltip has no available sampled values");
                    var plot=charts.Single();double x=plot.ActualWidth-13;
                    if(plot.InputHitTest(new Point(x,30))!=plot||plot.InputHitTest(new Point(x,plot.ActualHeight-35))!=plot)throw new InvalidOperationException("Empty chart area must receive pointer input");
                    plot.InspectPointerAt(new Point(x,30));await Task.Delay(30);var inspected=plot.InspectedTimestamp;
                    plot.InspectPointerAt(new Point(x,plot.ActualHeight-35));await Task.Delay(30);
                    if(!inspected.HasValue||plot.InspectedTimestamp!=inspected)throw new InvalidOperationException("Chart inspection must depend on time position, not trace height");
                    if(!design){plot.InspectAt(0);await Task.Delay(30);if(plot.InspectedTimestamp.HasValue)throw new InvalidOperationException("Unrecorded plot time must not show an unrelated sample");}
                    plot.InspectAt(1);await Task.Delay(30);
                    var lastInspected=plot.InspectedTimestamp;plot.Focus();
                    plot.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(plot)!,0,System.Windows.Input.Key.Left){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});await Task.Delay(30);
                    if(plot.InspectedTimestamp>=lastInspected)throw new InvalidOperationException("Chart keyboard inspection did not move to the previous sample");
                    plot.InspectPointerAt(new Point(x,5));await Task.Delay(30);if(plot.InspectedTimestamp.HasValue)throw new InvalidOperationException("Tooltip did not clear outside the plot");
                    plot.InspectAt(1);await Task.Delay(30);}
                string file=name.ToLowerInvariant().Replace(' ','-');Capture(System.IO.Path.Combine(directory,file+".png"));captures.Add(file);
                OwnWindowCapture.Save(this,System.IO.Path.Combine(directory,file+"-window.png"));Step("captured "+name);
                if(acerFixture&&name=="Sensors"){
                    var labels=VisualChildren(Root).OfType<TextBlock>().Select(t=>t.Text).ToArray();
                    if(labels.Any(t=>t.Contains("Firmware",StringComparison.OrdinalIgnoreCase))||labels.Contains("Package")||labels.Contains("Additional sensors"))throw new InvalidOperationException("CPU source wording or duplicate sensor card leaked into the normal UI");
                    var cpuValue=VisualChildren(Root).OfType<TextBlock>().Single(t=>System.Windows.Automation.AutomationProperties.GetAutomationId(t)=="cpu.temperature");
                    if(cpuValue.Text!="65 °C")throw new InvalidOperationException("CPU temperature value missing from the shared table");
                    var scroll=VisualChildren(Root).OfType<ScrollViewer>().Single();scroll.ScrollToEnd();await Task.Delay(150);OwnWindowCapture.Save(this,System.IO.Path.Combine(directory,"acer-fans-window.png"));scroll.ScrollToHome();
                }

            }
            if(design){
                latest=latest! with {Devices=latest.Devices.Select(d=>d.Id=="cpu"?d with {Temperature=null,State=ReadingState.Unavailable,Detail="Hardware access not enabled"}:d).ToArray()};
                Navigate("Sensors");await Task.Delay(200);Capture(System.IO.Path.Combine(directory,"sensors-unavailable.png"));
            }
            Navigate("Overview");await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
            var inspect=VisualChildren(Root).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Inspect CPU load");
            inspect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(200);
            if(page!="History"||charts.Single().Metric!=SparkMetric.Load)throw new InvalidOperationException("Overview CPU trend did not open load history");
            charts.Single().InspectAt(1);await Task.Delay(50);if(charts.Single().InspectedValueCount<1)throw new InvalidOperationException("Load history inspection has no values");
            Capture(System.IO.Path.Combine(directory,"history-load.png"));OwnWindowCapture.Save(this,System.IO.Path.Combine(directory,"history-load-window.png"));
            VisualChildren(Root).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Memory").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(200);
            if(charts.Single().Metric!=SparkMetric.MemoryUsage)throw new InvalidOperationException("Memory history selector failed");
            charts.Single().InspectAt(1);await Task.Delay(50);if(charts.Single().InspectedValueCount!=1)throw new InvalidOperationException("Memory history must contain one real RAM usage series");
            Capture(System.IO.Path.Combine(directory,"history-memory.png"));OwnWindowCapture.Save(this,System.IO.Path.Combine(directory,"history-memory-window.png"));
            historyMetric=SparkMetric.Temperature;Navigate("Overview");
            double defaultWidth=Width;Width=620;await Task.Delay(250);Capture(System.IO.Path.Combine(directory,"narrow-overview.png"));
            if(!layoutKey.Contains(":narrow:",StringComparison.Ordinal))throw new InvalidOperationException("Narrow overview breakpoint failed");
            Navigate("History");await Task.Delay(200);Capture(System.IO.Path.Combine(directory,"narrow-history.png"));
            var temperature=VisualChildren(Root).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Temperature");
            var period=VisualChildren(Root).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="1m");
            if(period.TranslatePoint(new Point(),Root).Y<temperature.TranslatePoint(new Point(),Root).Y+temperature.ActualHeight)throw new InvalidOperationException("Narrow history selector groups overlap");
            Width=defaultWidth;Navigate("Overview");await Task.Delay(250);
            CaptureElement(Root,System.IO.Path.Combine(directory,"overview-200-percent-raster.png"),2);
            if(acerFixture){latest=AcerTelemetryProtocol.Merge(latest!,new AcerSample(latest!.At,new(65,ReadingState.Available),new(null,ReadingState.Unsupported),new(null,ReadingState.Unsupported),new(null,ReadingState.Unsupported),LocalAcerReaderHost.CpuOnlyDetail));layoutKey="";Render();}
            CompactButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(900);
            CompactButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(!pinned||!Topmost)throw new InvalidOperationException("Compact pin failed");
            CompactButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(pinned||Topmost)throw new InvalidOperationException("Compact unpin failed");
            if(!System.Windows.Automation.AutomationProperties.GetName(CompactButton).Contains("Always on top: off"))throw new InvalidOperationException("Compact pin state is not explained");
            if(Math.Abs(ActualWidth-380)>2||ActualHeight>620)throw new InvalidOperationException("Compact dimensions did not settle");
            Capture(System.IO.Path.Combine(directory,"compact.png"));OwnWindowCapture.Save(this,System.IO.Path.Combine(directory,"compact-window.png"));captures.Add("compact");
            using(var menuScope=new VerificationMenu(CreateTrayMenu())){
                var menu=menuScope.Window;menu.HoldOpenForVerification=true;menu.Left=Left+Width+18;menu.Top=Top+360;menu.Owner=this;menu.Show();menu.Activate();await Task.Delay(350);
                CaptureElement(menu.Surface,System.IO.Path.Combine(directory,"tray-menu.png"));OwnWindowCapture.Save(menu,System.IO.Path.Combine(directory,"tray-menu-window.png"));captures.Add("tray-menu");Step("captured tray");
            }
            Step("menu disposed");Activate();ExpandButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(compact)throw new InvalidOperationException("Expand did not leave compact mode");Navigate("Overview");Step("restored overview");
            Step("start opaque");bool originalTransparency=transparency;transparency=false;ApplyBackdrop();Navigate("Overview");await Task.Delay(250);Capture(System.IO.Path.Combine(directory,"opaque-overview.png"));
            if(material.BackdropEnabled)throw new InvalidOperationException("Opaque setting did not disable backdrop");
            transparency=originalTransparency;ApplyBackdrop();
            Step("opaque restored");var realMaterial=material;
            if(material.SystemTransparency&&!material.HighContrast&&!material.BackdropEnabled)throw new InvalidOperationException("Supported glass backdrop did not enable");
            Step("start high contrast");material=GlassMaterial.Apply(this,Root,transparency,true);Step("high contrast applied");layoutKey="";Render();await Task.Delay(200);Capture(System.IO.Path.Combine(directory,"high-contrast-overview.png"));
            Step("high contrast captured");if(material.BackdropEnabled||!material.HighContrast)throw new InvalidOperationException("High contrast material fallback failed");
            ApplyBackdrop();layoutKey="";Render();
            Navigate("Settings");await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
            Step("settings layout settled");
            var durationControl=VisualChildren(Root).OfType<ComboBox>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Session history duration");
            durationControl.SelectedIndex=0;if(history.Retention!=TimeSpan.FromHours(1))throw new InvalidOperationException("One-hour retention selection failed");
            durationControl.SelectedIndex=1;if(history.Retention!=TimeSpan.FromHours(4))throw new InvalidOperationException("Four-hour retention selection failed");var samplingControl=VisualChildren(Root).OfType<ComboBox>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Sampling interval");samplingControl.SelectedIndex=2;
            if(interval!=5)throw new InvalidOperationException("Sampling control did not update interval");
            samplingControl.IsDropDownOpen=true;await Task.Delay(200);
            var popup=(System.Windows.Controls.Primitives.Popup)samplingControl.Template.FindName("PART_Popup",samplingControl);
            CaptureElement((FrameworkElement)popup.Child,System.IO.Path.Combine(directory,"sampling-menu.png"));
            samplingControl.IsDropDownOpen=false;samplingControl.SelectedIndex=1;
            Navigate("Sensors");await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
            var cpuToggle=VisualChildren(Root).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Toggle "+DeviceLabel(latest!.Devices.First(d=>d.Id=="cpu"),false));
            cpuToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(!collapsed.TryGetValue("cpu",out bool hidden)||!hidden)throw new InvalidOperationException("Sensor collapse failed");
            Capture(System.IO.Path.Combine(directory,"sensors-collapsed.png"));await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
            VisualChildren(Root).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Toggle "+DeviceLabel(latest!.Devices.First(d=>d.Id=="cpu"),false)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Navigate("Overview");SystemCommands.MinimizeWindow(this);await Task.Delay(150);
            string frozenStatus=Status.Text;var newer=latest! with {At=latest!.At.AddSeconds(1)};latest=newer;history.Add(newer);Render();
            if(Status.Text!=frozenStatus)throw new InvalidOperationException("Hidden window unnecessarily refreshed visuals");
            RestoreWindow();await Task.Delay(150);
            if(!Status.Text.Contains(newer.At.LocalDateTime.ToString("HH:mm:ss")))throw new InvalidOperationException("Restored window did not refresh latest reading");
            if(WindowState!=WindowState.Normal||!IsVisible||tray.Visible)throw new InvalidOperationException("Minimize and tray restore failed");
            if(design){
                latest=latest! with {Devices=latest.Devices.Concat(Enumerable.Range(1,6).Select(i=>new Reading("stress-"+i,"Very long device model name with multiple descriptors "+i+" "+new string('X',200),45+i,10+i,ReadingState.Available,"Visual stress fixture",latest.At))).ToArray()};
                Navigate("Sensors");Width=620;Height=550;await Task.Delay(400);Capture(System.IO.Path.Combine(directory,"minimum-size-many-devices.png"));
            }
            Step("local logging/export controls");
            Navigate("Settings");await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
            var logToggle=VisualChildren(Root).OfType<CheckBox>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Local CSV logging");
            logToggle.IsChecked=true;logToggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));await Task.Delay(100);
            if(!loggingRequested||!localLog.IsEnabled)throw new InvalidOperationException("Local logging toggle did not enable");
            var loggingSample=latest!;if(!localLog.TryWrite(loggingSample))throw new InvalidOperationException("Logger did not accept a sample");
            await ChangeLogging(false);if(loggingRequested||localLog.IsEnabled||localLog.WrittenSamples!=1)throw new InvalidOperationException("Disable did not drain logging");
            var logFile=Directory.GetFiles(localLog.DirectoryPath,"*.csv").Single();if(!File.ReadAllText(logFile).Contains("temperature_c"))throw new InvalidOperationException("Logged CSV schema missing");
            await ExportHistoryTo(System.IO.Path.Combine(directory,"history-export.csv"),history.Samples);
            if(!File.Exists(System.IO.Path.Combine(directory,"history-export.csv"))||exportBusy||!exportFeedback.StartsWith("CSV saved",StringComparison.Ordinal))throw new InvalidOperationException("Retained history export failed");
            exportFeedback="";
            Step("typography/icon matrix");
            foreach(string kind in PhosphorIcon.AvailableKinds){
                var icon=new PhosphorIcon{Kind=kind,Width=24,Height=24};icon.Measure(new Size(24,24));icon.Arrange(new Rect(0,0,24,24));
                var pixels=new RenderTargetBitmap(24,24,96,96,PixelFormats.Pbgra32);pixels.Render(icon);var data=new byte[24*24*4];pixels.CopyPixels(data,24*4,0);
                if(!Enumerable.Range(0,24*24).Any(i=>data[i*4+3]!=0))throw new InvalidOperationException("Empty icon: "+kind);
            }
            foreach(double width in new[]{620d,739d,740d,741d,850d,1024d,1440d}){
                Width=width;Height=680;await Task.Delay(30);
                foreach(string view in new[]{"Overview","History","Sensors","Specs","Settings","First run","Licences"}){
                    Navigate(view);await Task.Delay(60);await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ContextIdle);
                    if(Math.Abs(ActualWidth-width)>2||Math.Abs(Root.ActualWidth-width)>2)throw new InvalidOperationException($"Requested width {width}, actual window {ActualWidth}, root {Root.ActualWidth}, Width property {Width}");
                    var title=VisualChildren(Root).OfType<TextBlock>().Single(t=>Equals(t.Tag,"PageTitle"));
                    if(title.FontSize!=20||title.FontWeight!=FontWeights.SemiBold||title.TranslatePoint(new Point(),ContentHost).X!=0)throw new InvalidOperationException("Page title role/position differs on "+view);
                    if(title.ActualHeight>32||title.DesiredSize.Width>ContentHost.ActualWidth)throw new InvalidOperationException("Page title clipped on "+view);
                    foreach(var section in VisualChildren(Root).OfType<TextBlock>().Where(t=>Equals(t.Tag,"SectionTitle")))if(section.FontSize!=14||section.FontSize>=title.FontSize)throw new InvalidOperationException("Section hierarchy inverted");
                    foreach(var scroll in VisualChildren(Root).OfType<ScrollViewer>())if(scroll.ScrollableWidth>1)throw new InvalidOperationException("Horizontal overflow on "+view);
                    foreach(var bar in VisualChildren(Root).OfType<System.Windows.Controls.Primitives.ScrollBar>())if(bar.Visibility==Visibility.Visible&&bar.ActualWidth>7)throw new InvalidOperationException("Scrollbar is stealing content spacing");
                    VerifyIconSpacing(Root);if(Height!=680)throw new InvalidOperationException("Changing pages resized the window");
                    if(width is 620 or 850)Capture(System.IO.Path.Combine(directory,$"type-{view.ToLowerInvariant().Replace(' ','-')}-{width:0}.png"));
                }
            }
            SetCompact(true);Width=340;await Task.Delay(100);UpdateLayout();
            foreach(var grid in VisualChildren(Root).OfType<Grid>().Where(g=>System.Windows.Automation.AutomationProperties.GetAutomationId(g).EndsWith(".compact.temperature",StringComparison.Ordinal))){
                var id=System.Windows.Automation.AutomationProperties.GetAutomationId(grid).Replace(".compact.temperature","");
                var load=VisualChildren(Root).OfType<Grid>().Single(g=>System.Windows.Automation.AutomationProperties.GetAutomationId(g)==id+".compact.load");
                if(Math.Abs(grid.TranslatePoint(new Point(),ContentHost).Y-load.TranslatePoint(new Point(),ContentHost).Y)>1)throw new InvalidOperationException("Compact temperature/load are not aligned");
                if(grid.TranslatePoint(new Point(grid.ActualWidth,0),ContentHost).X>load.TranslatePoint(new Point(),ContentHost).X-3)throw new InvalidOperationException("Compact temperature/load overlap");
                foreach(var metric in new[]{grid,load}){
                    var parts=metric.Children.OfType<TextBlock>().ToArray();
                    if(parts[1].Visibility==Visibility.Visible&&Math.Abs(parts[0].TranslatePoint(new Point(0,parts[0].ActualHeight),metric).Y-parts[1].TranslatePoint(new Point(0,parts[1].ActualHeight),metric).Y)>7)throw new InvalidOperationException("Compact units detached from their numbers");
                }
            }
            if(VisualChildren(Root).OfType<TextBlock>().Any(t=>t.Text.Contains("Firmware",StringComparison.OrdinalIgnoreCase)||t.Text.Contains("load ·",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Compact source wording or combined metric returned");
            if(VisualChildren(Root).OfType<ScrollViewer>().Any(v=>v.ScrollableWidth>1))throw new InvalidOperationException("Compact horizontal overflow");
            Capture(System.IO.Path.Combine(directory,"compact-minimum.png"));OwnWindowCapture.Save(this,System.IO.Path.Combine(directory,"compact-minimum-window.png"));SetCompact(false);
            if(Width!=1440||Height!=680)throw new InvalidOperationException("Compact mode lost normal window size");
            Width=850;Navigate("Overview");
            Step("writing result");File.WriteAllText(System.IO.Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{DesignFixture=design,AcerFixture=acerFixture,SettingsVersionVerified=true,PauseControlsRemovedVerified=true,FocusMaterialStableVerified=true,FrameShadowDisabledVerified=true,UnusedCpuAssembliesAbsentVerified=true,AcerHardwareCalls=0,TypographyMatrixVerified=true,CpuLabelsUnifiedVerified=true,CompactColumnsAlignedVerified=true,IconCatalogVerified=true,NormalWindowSizePreserved=true,IconSpacingVerified=true,LocalLoggingDefaultOffVerified=true,LocalLoggingControlVerified=true,CsvExportVerified=true,LiveBaselineVerified=true,SpecsDetectionVerified=true,MetricHistoryVerified=true,ChartFullAreaPointerVerified=true,UnitSpacingVerified=true,NeutralClickFocusVerified=true,HistoryRetentionControlVerified=true,TrendNavigationVerified=true,NarrowLayoutVerified=true,InitialSamplingSeconds=1,live.CpuLoad,live.UsedRamGb,live.TotalRamGb,LiveDevices=live.Devices,Samples=history.Samples.Count,Captures=captures,PauseResumeVerified=true,PausedSnapshotStable=true,CompactPinExpandVerified=true,SamplingControlVerified=true,SensorCollapseVerified=true,TrayRestoreVerified=true,HistoryTooltipValuesVerified=true,DisplayDpi=VisualTreeHelper.GetDpi(this).PixelsPerDip,Material=realMaterial,HighContrastFallbackVerified=true,ThisAppCpuDriverAccess=false,Checks=new[]{"Live baseline","Pause and resume feedback","All reference surfaces","Compact dimensions and pin/expand","Sampling dropdown","Sensor collapse","Minimize / tray restore","Opaque backdrop off","High contrast palette fallback","History cursor","No CPU driver access"}},new JsonSerializerOptions{WriteIndented=true}));
            Close();
        }catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(System.IO.Path.Combine(directory,"error.txt"),ex.ToString());Close();}
        finally{watchdog.Stop();Application.Current.DispatcherUnhandledException-=report;Step("closing stage");stage?.Close();}
    }
    private static void VerifyIconSpacing(DependencyObject root){
        foreach(var panel in VisualChildren(root).OfType<StackPanel>().Where(p=>p.Orientation==Orientation.Horizontal)){
            var children=panel.Children.OfType<FrameworkElement>().ToArray();
            for(int i=0;i<children.Length-1;i++)if(children[i] is PhosphorIcon icon&&children[i+1] is TextBlock label){
                double gap=label.TranslatePoint(new Point(),panel).X-icon.TranslatePoint(new Point(icon.ActualWidth,0),panel).X;
                if(gap<7.5)throw new InvalidOperationException("Inline icon/text gap too small: "+gap);
            }
        }
        foreach(var grid in VisualChildren(root).OfType<Grid>()){
            var children=grid.Children.OfType<FrameworkElement>().Where(c=>c.Visibility==Visibility.Visible&&Grid.GetRow(c)==0).OrderBy(Grid.GetColumn).ToArray();
            for(int i=0;i<children.Length-1;i++){
                var box=children[i];bool icon=box is PhosphorIcon||box is Border border&&border.Child is PhosphorIcon;
                if(!icon||Grid.GetColumn(children[i+1])<=Grid.GetColumn(box))continue;
                double gap=children[i+1].TranslatePoint(new Point(),grid).X-box.TranslatePoint(new Point(box.ActualWidth,0),grid).X;
                if(gap<7.5)throw new InvalidOperationException("Grid icon/text gap too small: "+gap);
            }
        }
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in VisualChildren(child))yield return descendant;}
    }
    public void Capture(string destination)=>CaptureElement(Root,destination);
    public static void CaptureElement(FrameworkElement element,string destination,double scale=1){
        element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*scale),(int)Math.Ceiling(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(destination);encoder.Save(stream);
    }
    private Window CreateVerificationStage(){
        // Controlled, original backdrop for own-window verification. Never part of normal UI.
        var grid=new Grid{Background=new SolidColorBrush(Color.FromRgb(42,44,47))};
        return new Window{Width=Width+370,Height=Height+160,Left=Left-60,Top=Top-55,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Content=grid};
    }
    private static IEnumerable<Snapshot> DesignSamples(){
        var end=DateTimeOffset.Now;for(int i=0;i<=180;i++){
            var at=end-TimeSpan.FromSeconds((180-i)*5);double gpu=51+Math.Sin(i*.13)*2;
            yield return new(at,18,12.4,32,[new("cpu","Example CPU",null,18,ReadingState.Unavailable,"Visual test fixture · temperature unavailable",at),new("nvidia-0","Example NVIDIA GPU",gpu,6,ReadingState.Available,"Visual test fixture · GPU core",at),new("nvidia-1","Second GPU with a deliberately long model name to test layout",gpu+3,8,ReadingState.Available,"Visual test fixture · second GPU",at),new("display-0","Example integrated graphics",null,null,ReadingState.Unsupported,"Readings not exposed by a supported provider",at)]);
        }
    }
    private sealed class VerificationMenu(TrayMenuWindow window):IDisposable{public TrayMenuWindow Window=>window;public void Dispose()=>window.Dismiss();}
}
