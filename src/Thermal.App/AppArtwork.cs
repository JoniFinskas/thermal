using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Thermal.App;

// Phosphor thermometer rendered once for title/tray. Upstream MIT notice ships with the app.
internal static class AppArtwork {
    private static byte[] Render(string ink){
        var glyph=new PhosphorIcon{Kind="Thermometer",Ink=ink,Width=32,Height=32};glyph.Measure(new Size(32,32));glyph.Arrange(new Rect(0,0,32,32));glyph.UpdateLayout();
        var bitmap=new RenderTargetBitmap(32,32,96,96,PixelFormats.Pbgra32);bitmap.Render(glyph);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();png.Save(stream);return stream.ToArray();
    }
    public static ImageSource CreateWindowIcon(){using var stream=new MemoryStream(Render("Accent"));var source=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);source.Freeze();return source;}
    public static System.Drawing.Icon CreateTrayIcon(){
        using var stream=new MemoryStream(Render("Ink"));using var bitmap=new System.Drawing.Bitmap(stream);IntPtr icon=bitmap.GetHicon();
        try{using var wrapper=System.Drawing.Icon.FromHandle(icon);return (System.Drawing.Icon)wrapper.Clone();}finally{DestroyIcon(icon);}
    }
    [DllImport("user32")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DestroyIcon(IntPtr icon);
}
