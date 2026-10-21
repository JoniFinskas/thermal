using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Thermal.App;

// QA captures only the requesting app's own HWND through PrintWindow. No desktop enumeration,
// general screenshot service, input injection, external window control or network export.
internal static class OwnWindowCapture {
    public static void Save(Window ownWindow,string destination){
        var hwnd=new WindowInteropHelper(ownWindow).Handle;
        GetWindowThreadProcessId(hwnd,out uint processId);
        if(hwnd==IntPtr.Zero||processId!=(uint)Environment.ProcessId)throw new InvalidOperationException("Capture target must belong to this app process");
        if(!GetWindowRect(hwnd,out var rect))throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        if(width<=0||height<=0||width>4096||height>4096)throw new InvalidOperationException("Own-window capture bounds invalid");
        var source=GetWindowDC(hwnd);if(source==IntPtr.Zero)throw new InvalidOperationException("Own-window DC unavailable");
        IntPtr memory=IntPtr.Zero,bitmap=IntPtr.Zero,old=IntPtr.Zero;
        try{
            memory=CreateCompatibleDC(source);bitmap=CreateCompatibleBitmap(source,width,height);
            if(memory==IntPtr.Zero||bitmap==IntPtr.Zero)throw new InvalidOperationException("Own capture allocation failed");
            old=SelectObject(memory,bitmap);
            if(!PrintWindow(hwnd,memory,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            using var image=System.Drawing.Image.FromHbitmap(bitmap);image.Save(destination,System.Drawing.Imaging.ImageFormat.Png);
        }finally{if(old!=IntPtr.Zero)SelectObject(memory,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(memory!=IntPtr.Zero)DeleteDC(memory);ReleaseDC(hwnd,source);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32")]private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32")]private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32")]private static extern bool DeleteDC(IntPtr dc);
}
