using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Thermal.Core;
using Forms=System.Windows.Forms;
namespace Thermal.App;

public partial class MainWindow : Window {
    private readonly WindowsMetrics baseline=new();
    private readonly History history=new(TimeSpan.FromHours(4));
    private readonly CancellationTokenSource stop=new();
    private readonly List<Action> refresh=new();
    private readonly List<TrendChart> charts=new();
    private readonly List<MetricSparkline> sparklines=new();
    private readonly Dictionary<string,bool> collapsed=new();
    private Snapshot? latest;
    private string page="Overview",layoutKey="";
    private volatile bool paused,systemSuspended;
    private int samplingGeneration;
    private bool compact,fahrenheit,transparency=true,pinned,verificationFreeze;
    private volatile int interval=2;
    private int minutes=15;
    private double normalWidth=850,normalHeight=680;
    private SparkMetric historyMetric=SparkMetric.Temperature;
    private readonly Forms.NotifyIcon tray=new();
    private System.Drawing.Icon? trayArtwork;
    private TrayMenuWindow? trayMenu;
    private readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Thermal","preferences.json");
    private MaterialResult material;

    public MainWindow() {
        InitializeComponent();
        if(Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal)))settingsPath=Path.Combine(AppContext.BaseDirectory,"work-verification","preferences.json");
        else LoadPreferences();
        bool verifying=Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal));
        localLog=new LocalCsvLog(verifying?Path.Combine(AppContext.BaseDirectory,"work-verification","logs-"+Guid.NewGuid().ToString("N")):Path.Combine(Path.GetDirectoryName(settingsPath)!,"Logs"));
        Closing+=FinishLoggingOnClose;
        trayArtwork=AppArtwork.CreateTrayIcon();tray.Icon=trayArtwork;tray.Text="Thermal";Icon=AppArtwork.CreateWindowIcon();
        tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Right)Dispatcher.Invoke(ShowTrayMenu);};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(RestoreWindow);
        Loaded+=async (_,_)=>{
            ApplyBackdrop();
            RequestAcerOnOpen();
            if(Environment.GetCommandLineArgs().Contains("--verify-render-stress")){await VerifyRenderStress();return;}
            if(Environment.GetCommandLineArgs().Contains("--verify-performance")){await VerifyPerformance();return;}
            if(Environment.GetCommandLineArgs().Contains("--verify-logging")){await VerifyLoggingPerformance();return;}
            if(Environment.GetCommandLineArgs().Contains("--verify-local-acer-reader")){await VerifyLocalAcerReader();return;}
#if !THERMAL_PUBLIC
            if(Environment.GetCommandLineArgs().Contains("--verify-acer-reader")){await VerifyAcerReader();return;}
#endif
            if(loggingRequested)localLog.Start();
            _=Task.Run(SampleLoop);Render();
            var args=Environment.GetCommandLineArgs();
#if !THERMAL_PUBLIC
            if(args.Contains("--observe-local-cpu")){await ObserveLocalCpu();return;}
#endif
            if(args.Contains("--verify-ui")||args.Contains("--verify-design")||args.Contains("--verify-acer"))await VerifyUi(args.Contains("--verify-design")||args.Contains("--verify-acer"));
        };
        StateChanged+=(_,_)=>{
            Root.CornerRadius=WindowState==WindowState.Maximized?new(0):new(12);
            if(WindowState==WindowState.Minimized){tray.Visible=true;Hide();}
        };
        Closed+=(_,_)=>{stop.Cancel();trayMenu?.Dismiss();tray.Dispose();trayArtwork?.Dispose();SavePreferences();SystemParameters.StaticPropertyChanged-=SystemSettingChanged;Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerModeChanged;};
        SystemParameters.StaticPropertyChanged+=SystemSettingChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerModeChanged;
        SizeChanged+=(_,_)=>{if(!compact){string responsive=ActualWidth<740?"narrow":"wide";if(!layoutKey.Contains(":"+responsive+":",StringComparison.Ordinal)){layoutKey="";Render();}}};
    }
    private async Task SampleLoop() {
        // At most one GPU call is in flight. A stalled native call cannot block baseline sampling.
        var gpuTask=Task.Run(()=>new NvidiaTelemetry());NvidiaTelemetry? gpu=null;
        int observedGeneration=Volatile.Read(ref samplingGeneration);
        int gpuGeneration=observedGeneration;Task<IReadOnlyList<Reading>>? pending=null;IReadOnlyList<Reading> gpuReadings=Array.Empty<Reading>();
        try {
            while(!stop.IsCancellationRequested) {
                int currentGeneration=Volatile.Read(ref samplingGeneration);
                if(currentGeneration!=observedGeneration){observedGeneration=currentGeneration;baseline.Reset();gpuReadings=Array.Empty<Reading>();}
                if(!paused&&!systemSuspended&&!verificationFreeze) {
                    if(gpuTask.IsCompletedSuccessfully)gpu=gpuTask.Result;else if(gpuTask.IsFaulted)_=gpuTask.Exception;
                    if(pending?.IsCompleted==true){if(gpuGeneration==currentGeneration&&pending.IsCompletedSuccessfully)gpuReadings=pending.Result;_=pending.Exception;pending=null;}
                    if(gpu!=null&&pending==null){gpuGeneration=currentGeneration;pending=Task.Run(gpu.Sample);}
                    var now=DateTimeOffset.UtcNow;
                    var readings=gpuReadings.Select(r=>now-r.At>TimeSpan.FromSeconds(interval*3)?r with {State=ReadingState.Stale,Temperature=null,Load=null,Detail="Reading is stale"}:r).ToArray();
                    var baselineSample=baseline.Sample(readings);
                    var sample=AcerTelemetryProtocol.Merge(baselineSample,acerReader?.Latest);
                    await Dispatcher.InvokeAsync(()=>{if(paused||systemSuspended||verificationFreeze||Volatile.Read(ref samplingGeneration)!=currentGeneration)return;latest=sample;history.Add(sample);localLog.TryWrite(sample);Render();});
                }
                await Task.Delay(TimeSpan.FromSeconds(interval),stop.Token);
            }
        }catch(OperationCanceledException){}catch(Exception ex){if(!stop.IsCancellationRequested)await Dispatcher.InvokeAsync(()=>Status.Text="Readings unavailable: "+ex.Message);}
        finally{
            // Dispose only after our own native work returns; never unload a DLL in use.
            if(pending is { } tail)_=tail.ContinueWith(t=>{_=t.Exception;gpu?.Dispose();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            else _=gpuTask.ContinueWith(t=>{if(t.IsCompletedSuccessfully)t.Result.Dispose();else _=t.Exception;},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
    }
    private void Render() {
        if(!IsVisible||WindowState==WindowState.Minimized)return;
        Status.Text=paused?"Readings paused":latest==null?"Reading sensors…":$"Updated {latest.At.LocalDateTime:HH:mm:ss}";
        string key=page+":"+(ActualWidth<740?"narrow":"wide")+":"+compact+":"+minutes+":"+historyMetric+":"+fahrenheit+":"+pinned+":"+acerEnabled+":"+(latest?.Acer!=null)+":"+string.Join(",",latest?.Devices.Select(d=>d.Id+":"+d.State+":"+d.Temperature.HasValue+":"+d.Load.HasValue)??Array.Empty<string>());
        if(key==layoutKey){foreach(var update in refresh)update();foreach(var c in charts){c.Samples=history.Samples;c.InvalidateVisual();}foreach(var c in sparklines){c.Samples=history.RecentSamples;c.InvalidateVisual();}return;}
        layoutKey=key;refresh.Clear();charts.Clear();sparklines.Clear();ContentHost.Children.Clear();
        var tabs=new[]{OverviewTab,HistoryTab,SensorsTab,SpecsTab,SettingsTab};
        string[] names=["Overview","History","Sensors","Specs","Settings"];
        for(int i=0;i<tabs.Length;i++){tabs[i].Tag=names[i]==page?"Selected":null;System.Windows.Automation.AutomationProperties.SetHelpText(tabs[i],names[i]==page?"Current page":"Open "+names[i]);}
        Navigation.Visibility=compact||page=="First run"?Visibility.Collapsed:Visibility.Visible;
        CompactButton.Content=compact?ButtonContent("Pin",pinned?"On top · On":"Keep on top"):new PhosphorIcon{Kind="Compact",Width=15,Height=15};
        CompactButton.Width=compact?125:36;
        CompactButton.Background=compact&&pinned?(Brush)FindResource("GroupHeading"):Brushes.Transparent;
        CompactButton.ToolTip=compact?(pinned?"Turn off always on top":"Keep this window above other apps"):"Compact mode";
        System.Windows.Automation.AutomationProperties.SetName(CompactButton,compact?(pinned?"Always on top: on. Turn off":"Always on top: off. Turn on"):"Compact mode");
        MaximizeButton.IsEnabled=!compact;
        ExpandButton.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        if(compact){
            Height=CompactWindowHeight;RenderCompact();return;
        }
        var stack=new StackPanel();
        var scroll=new ScrollViewer{Style=(Style)FindResource(typeof(ScrollViewer)),Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(0,0,1,0)};
        ContentHost.Children.Add(scroll);
        switch(page){case "Overview":RenderOverview(stack);break;case "History":RenderHistory(stack);break;case "Sensors":RenderSensors(stack);break;case "Specs":RenderSpecs(stack);break;case "Settings":RenderSettings(stack);break;case "Licences":RenderLicences(stack);break;default:RenderFirstRun(stack);break;}
    }
    private void Navigate(string next){
        if(compact)SetCompact(false);page=next;layoutKey="";
        if(WindowState==WindowState.Normal)MinHeight=550;
        Render();
    }
    private void OverviewClick(object sender,RoutedEventArgs e)=>Navigate("Overview");
    private void HistoryClick(object sender,RoutedEventArgs e)=>Navigate("History");
    private void SensorsClick(object sender,RoutedEventArgs e)=>Navigate("Sensors");
    private void SpecsClick(object sender,RoutedEventArgs e)=>Navigate("Specs");
    private void SettingsClick(object sender,RoutedEventArgs e)=>Navigate("Settings");
    private void MinimizeClick(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void MaximizeClick(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private void ExpandClick(object sender,RoutedEventArgs e)=>SetCompact(false);
    private void RestoreWindow(){Show();SystemCommands.RestoreWindow(this);WindowState=WindowState.Normal;Render();Activate();tray.Visible=false;}
    private void PauseClick(object? sender=null,RoutedEventArgs? e=null){paused=!paused;acerReader?.Pause(paused||systemSuspended);layoutKey="";Interlocked.Increment(ref samplingGeneration);Render();}
    private void CompactClick(object sender,RoutedEventArgs e){if(compact){pinned=!pinned;Topmost=pinned;layoutKey="";Render();}else SetCompact(true);}
    private void SetCompact(bool enabled){
        if(enabled&&!compact){normalWidth=WindowState==WindowState.Maximized?RestoreBounds.Width:Width;normalHeight=WindowState==WindowState.Maximized?RestoreBounds.Height:Height;}
        compact=enabled;layoutKey="";WindowState=WindowState.Normal;
        MinWidth=enabled?340:620;MinHeight=enabled?230:550;
        Width=enabled?380:normalWidth;
        Height=enabled?CompactWindowHeight:normalHeight;
        if(!enabled){pinned=false;Topmost=false;}
        Render();
    }
    private void ShowTrayMenu(){
        trayMenu?.Dismiss();trayMenu=CreateTrayMenu();
        var cursor=Forms.Cursor.Position;var work=Forms.Screen.FromPoint(cursor).WorkingArea;
        var dpi=VisualTreeHelper.GetDpi(this);double x=cursor.X/dpi.DpiScaleX,y=cursor.Y/dpi.DpiScaleY;
        trayMenu.Left=Math.Max(work.Left/dpi.DpiScaleX,Math.Min(x-trayMenu.Width,work.Right/dpi.DpiScaleX-trayMenu.Width));
        trayMenu.Top=Math.Max(work.Top/dpi.DpiScaleY,Math.Min(y-trayMenu.Height,work.Bottom/dpi.DpiScaleY-trayMenu.Height));
        trayMenu.Show();trayMenu.Activate();
    }
    private TrayMenuWindow CreateTrayMenu()=>new(transparency,compact,
        ()=>RestoreWindow(),()=>{SetCompact(!compact);RestoreWindow();},()=>{Navigate("Settings");RestoreWindow();},()=>Close());
    private void PowerModeChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e){
        if(e.Mode==Microsoft.Win32.PowerModes.Suspend)systemSuspended=true;
        else if(e.Mode==Microsoft.Win32.PowerModes.Resume)systemSuspended=false;
        else return;
        acerReader?.Pause(paused||systemSuspended);
        Interlocked.Increment(ref samplingGeneration);
    }
    private void SystemSettingChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName==nameof(SystemParameters.HighContrast))Dispatcher.Invoke(()=>{ApplyBackdrop();layoutKey="";Render();});}
    private void ApplyBackdrop(){material=GlassMaterial.Apply(this,Root,transparency);}
    private void LoadPreferences(){try{if(File.Exists(settingsPath)&&new FileInfo(settingsPath).Length<=16384){var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath));if(p!=null){interval=p.Interval is 1 or 2 or 5?p.Interval:2;fahrenheit=p.Fahrenheit;transparency=p.Transparency;loggingRequested=p.Logging;history.SetRetention(TimeSpan.FromHours(p.HistoryHours is 1 or 4? p.HistoryHours:4));}}}catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){} }
    private void SavePreferences(){try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);string tmp=settingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(new Preferences(interval,fahrenheit,transparency,(int)history.Retention.TotalHours,loggingRequested)));File.Move(tmp,settingsPath,true);}catch(IOException){}catch(UnauthorizedAccessException){} }
    public sealed record Preferences(int Interval,bool Fahrenheit,bool Transparency,int HistoryHours=4,bool Logging=false);
}
