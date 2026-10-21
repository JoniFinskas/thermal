using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
namespace Thermal.App;

public readonly record struct MaterialResult(bool BackdropEnabled,int BackdropResult,int FrameResult,bool SystemTransparency,bool HighContrast);
public static class GlassMaterial {
    public static bool HighContrastActive {get;private set;}
    public static MaterialResult Apply(Window window,Border root,bool requested,bool verificationHighContrast=false){
        bool systemTransparency=true;
        try{using var prefs=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");systemTransparency=prefs?.GetValue("EnableTransparency") is not int systemSetting||systemSetting!=0;}catch(System.Security.SecurityException){systemTransparency=false;}
        bool highContrast=SystemParameters.HighContrast||verificationHighContrast;HighContrastActive=highContrast;bool enabled=requested&&systemTransparency&&!highContrast;
        var hwnd=new WindowInteropHelper(window).Handle;
        int dark=1;DwmSetWindowAttribute(hwnd,20,ref dark,4);
        int corners=2;DwmSetWindowAttribute(hwnd,33,ref corners,4);
        // Desktop acrylic intentionally fades on deactivation. Use native blur with a fixed flat tint instead.
        int backdrop=1;DwmSetWindowAttribute(hwnd,38,ref backdrop,4);
        int nonClient=1;DwmSetWindowAttribute(hwnd,2,ref nonClient,4); // DWMNCRP_DISABLED: no active-window frame shadow.
        int backdropResult=ApplyBlur(hwnd,enabled);
        bool glass=enabled&&backdropResult==0;
        window.Background=glass?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(24,27,31));
        var source=HwndSource.FromHwnd(hwnd);if(source?.CompositionTarget!=null)source.CompositionTarget.BackgroundColor=glass?Colors.Transparent:Color.FromRgb(24,27,31);
        var margins=new Margins{Left=glass?-1:0};int frameResult=DwmExtendFrameIntoClientArea(hwnd,ref margins);
        var app=Application.Current.Resources;
        void Set(string key,Brush value)=>app[key]=value;
        Set("Ink",highContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Color.FromRgb(241,244,247)));
        Set("Muted",highContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Color.FromRgb(188,201,211)));
        Set("Accent",highContrast?SystemColors.HighlightBrush:new SolidColorBrush(Color.FromRgb(255,74,81)));
        Set("WarningInk",highContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Color.FromRgb(255,206,102)));
        Set("SuccessInk",highContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Color.FromRgb(113,224,123)));
        Set("Edge",highContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Color.FromArgb(40,255,255,255)));
        Set("Divider",highContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Color.FromArgb(32,255,255,255)));
        Set("GlassButton",highContrast?SystemColors.ControlBrush:new SolidColorBrush(Color.FromArgb(70,64,68,74)));
        Set("MenuSurface",highContrast?SystemColors.WindowBrush:new SolidColorBrush(Color.FromArgb(245,25,28,32)));
        Set("Surface",highContrast?SystemColors.WindowBrush:new SolidColorBrush(glass?Color.FromArgb(230,22,25,29):Color.FromRgb(22,25,29)));
        Set("GroupHeading",highContrast?SystemColors.WindowBrush:new SolidColorBrush(Color.FromArgb(50,70,74,81)));
        root.Background=highContrast?SystemColors.WindowBrush:new SolidColorBrush(glass?Color.FromArgb(190,24,27,31):Color.FromRgb(24,27,31));
        root.BorderBrush=highContrast?SystemColors.WindowTextBrush:(Brush)app["ShellEdge"];
        return new(glass,backdropResult,frameResult,systemTransparency,highContrast);
    }
    private static int ApplyBlur(IntPtr hwnd,bool enabled){
        // Optional Windows composition API. Unsupported systems keep the opaque accessibility-safe fallback.
        var policy=new AccentPolicy{State=enabled?3:0};
        IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try{Marshal.StructureToPtr(policy,memory,false);var data=new CompositionData{Attribute=19,Data=memory,Size=(nuint)Marshal.SizeOf<AccentPolicy>()};return SetWindowCompositionAttribute(hwnd,ref data)?0:-1;}
        catch(EntryPointNotFoundException){return -1;}
        finally{Marshal.FreeHGlobal(memory);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct AccentPolicy{public int State,Flags,Color,Animation;}
    [StructLayout(LayoutKind.Sequential)]private struct CompositionData{public int Attribute;public IntPtr Data;public nuint Size;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [StructLayout(LayoutKind.Sequential)]private struct Margins{public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi")]private static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
    [DllImport("dwmapi")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
