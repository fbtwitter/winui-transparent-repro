using System.Runtime.InteropServices;

namespace TransparentWindowRepro;

internal static class NativeMethods
{
    // SetWindowPos / always-on-top
    internal static readonly IntPtr HWND_TOPMOST  = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOSIZE     = 0x0001;
    internal const uint SWP_NOMOVE     = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    // Window style manipulation
    internal const int  GWL_STYLE   = -16;
    internal const int  GWL_EXSTYLE = -20;
    internal const uint WS_BORDER      = 0x00800000;
    internal const uint WS_DLGFRAME    = 0x00400000;
    internal const uint WS_THICKFRAME  = 0x00040000;
    internal const uint WS_EX_WINDOWEDGE          = 0x00000100;
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    // GDI class background brush removal
    internal const int GCLP_HBRBACKGROUND = -10;
    internal const int NULL_BRUSH         = 5;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr GetStockObject(int fnObject);

    // Enumerate child HWNDs created by the XAML island
    internal delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    // DWM — per-pixel alpha and frame extension
    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    // Windows 11 DWM attributes
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const uint DWMWCP_DONOTROUND              = 1;
    internal const uint DWMWA_BORDER_COLOR             = 34;
    internal const uint DWMWA_COLOR_NONE               = 0xFFFFFFFE;

    // Window messages
    internal const uint WM_ERASEBKGND   = 0x0014;
    internal const uint WM_DISPLAYCHANGE = 0x007E;

    // comctl32 — native window subclassing (intercept WM_ERASEBKGND / WM_DISPLAYCHANGE
    // without polling; the delegate must be kept alive to prevent GC of the function pointer)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    internal static extern bool SetWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    internal static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    internal static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
