using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace Thermal.App;
public partial class MainWindow {
    private async Task VerifyReleaseSurface(string directory){
        static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
        Navigate("Overview");UpdateLayout();
        if(Walk(ContentHost).OfType<Button>().Any(b=>b.Content?.ToString() is "Pause" or "Resume"))throw new InvalidOperationException("Removed pause control still visible");
        var menu=CreateTrayMenu();try{if(Walk(menu.Surface).OfType<TextBlock>().Any(t=>t.Text.Contains("Pause readings")||t.Text.Contains("Resume readings")))throw new InvalidOperationException("Tray still exposes pause");}finally{menu.Close();}
        Navigate("Settings");UpdateLayout();
        var version=Walk(ContentHost).OfType<TextBlock>().Single(t=>AutomationProperties.GetAutomationId(t)=="AppVersion");
        if(version.Text!="Version "+AppVersion.Current||AppVersion.Current=="Unknown")throw new InvalidOperationException("Settings version does not match the assembly");
        var versionBounds=version.TransformToAncestor(ContentHost).TransformBounds(new Rect(version.RenderSize));
        if(!version.IsVisible||version.ActualWidth<=0||version.ActualHeight<=0||versionBounds.Top<0||versionBounds.Bottom>ContentHost.ActualHeight)throw new InvalidOperationException("Settings version is outside the visible viewport");
        if(typeof(App).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "Thermal.CpuSensors" or "Thermal.CpuService"))throw new InvalidOperationException("Unused CPU service still linked");
        Capture(Path.Combine(directory,"settings-version.png"));Navigate("Overview");
        bool frozen=verificationFreeze;verificationFreeze=true;
        try{
            Activate();await Task.Delay(150);bool activeBefore=IsActive;var tint=((SolidColorBrush)Root.Background).Color;
            var original=material;var hwnd=new WindowInteropHelper(this).Handle;
            if(DwmGetWindowAttribute(hwnd,1,out int frameEnabled,4)!=0||frameEnabled!=0)throw new InvalidOperationException("Native frame shadow policy was not disabled");
            Capture(Path.Combine(directory,"focus-active.png"));
            using var other=new FocusVerificationWindow{Left=Left+Width+12,Top=Top,Owner=this};other.Show();other.Activate();await Task.Delay(200);
            bool inactive=!IsActive;
            Capture(Path.Combine(directory,"focus-inactive.png"));
            if(material!=original||((SolidColorBrush)Root.Background).Color!=tint)throw new InvalidOperationException("Focus changed the app material");
            other.Close();Activate();await Task.Delay(150);
            if(!activeBefore||!inactive)throw new InvalidOperationException($"Focus states were not exercised: active={activeBefore}, inactive={inactive}");
        }finally{verificationFreeze=frozen;}
    }
    private sealed class FocusVerificationWindow:Window,IDisposable {
        public FocusVerificationWindow(){Width=180;Height=70;ShowInTaskbar=false;Content=new TextBlock{Text="Thermal focus check",Margin=new Thickness(12)};}
        public void Dispose()=>Close();
    }
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
}
