using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinUIEx;

namespace TransparentWindowRepro;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;

    // Drag state
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private PointInt32 _windowStartPos;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE          = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    public MainWindow()
    {
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        // WORKAROUND 1: Hide before InitializeComponent.
        //
        // On dark theme the compositor paints a black default background fill during
        // window creation. On light theme it paints white. Either bleeds through if
        // we let the window become visible before TransparentTintBackdrop is applied.
        // Hiding here prevents that flash. We show again at the end of OnFirstActivated.
        ShowWindow(_hwnd, SW_HIDE);

        // WORKAROUND 2: Apply WS_EX_NOREDIRECTIONBITMAP BEFORE InitializeComponent.
        //
        // After InitializeComponent, the XAML island allocates a GDI redirection bitmap.
        // Setting WS_EX_NOREDIRECTIONBITMAP after that point is too late on SDR displays:
        // the bitmap is already created and the white backing bleeds through transparent
        // areas even after the flag is applied.
        ApplyWindowStyles();

        InitializeComponent();

        // WORKAROUND 3: SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false).
        //
        // Using hasBorder: false causes DWM to inject WS_DLGFRAME on Windows 10 22H2,
        // which produces a thin white border around the window even after we strip
        // all border style bits. Passing true and then stripping the bits in
        // ApplyWindowStyles() avoids the artifact.
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable   = false;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // WORKAROUND 4: ExtendsContentIntoTitleBar = true.
        //
        // Without this, WinUI 3 does not extend the XAML content root to fill the
        // entire HWND. The top portion (title bar area) remains the underlying GDI
        // window background (white), visible as a white stripe above the XAML content.
        ExtendsContentIntoTitleBar = true;

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(new SizeInt32(160, 160));

        // Always-on-top so the overlay floats above other windows.
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            200, 200, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // WORKAROUND 5: Re-apply styles after all AppWindow.* calls.
        //
        // SetPresenter() and ExtendsContentIntoTitleBar both modify GWL_EXSTYLE and
        // can silently clear WS_EX_NOREDIRECTIONBITMAP. ApplyWindowStyles() must run
        // after every call that touches GWL_EXSTYLE.
        ApplyWindowStyles();

        // WORKAROUND 6: Defer SystemBackdrop to first Activated event.
        //
        // Setting SystemBackdrop in the constructor races against DWM/WarpPal
        // initialisation and causes a null vtable crash (0xC0000005 access violation).
        // The backdrop must be applied after the compositor is ready, which is
        // signalled by the first Activated event.
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;

        SystemBackdrop = new TransparentTintBackdrop();

        // WORKAROUND 7: Re-apply styles after setting the backdrop.
        //
        // TransparentTintBackdrop's internal backdrop controller modifies GWL_EXSTYLE,
        // silently clearing WS_EX_NOREDIRECTIONBITMAP. It also resets
        // DwmExtendFrameIntoClientArea. ApplyWindowStyles() must run here to restore them.
        ApplyWindowStyles();

        // WORKAROUND 8: Patch XAML island child HWNDs via EnumChildWindows.
        //
        // WinUI 3 creates several internal child windows during XAML initialisation
        // (ContentIsland host, input source, SurfacePresenter, etc.). Each inherits
        // the white HWND class background brush from the Win32 window class, and each
        // paints that white fill independently. They are only available after the first
        // Activated event — the constructor is too early.
        //
        // Without this patch, white rectangles are visible behind semi-transparent
        // XAML elements regardless of WS_EX_NOREDIRECTIONBITMAP on the root HWND.
        NativeMethods.EnumChildWindows(_hwnd, (childHwnd, _) =>
        {
            NativeMethods.SetClassLongPtr(
                childHwnd,
                NativeMethods.GCLP_HBRBACKGROUND,
                NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH));
            return true;
        }, IntPtr.Zero);

        // Backdrop and styles are settled — reveal the window.
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    private void ApplyWindowStyles()
    {
        // Remove GDI class background brush on the root HWND.
        // NULL_BRUSH (stock object 5) = no background paint, preventing the OS from
        // filling the window with white before the DComp visual tree is composited.
        NativeMethods.SetClassLongPtr(
            _hwnd,
            NativeMethods.GCLP_HBRBACKGROUND,
            NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH));

        // Flush any pending presenter/style changes to DWM before we modify styles.
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);

        // Strip all frame style bits so no visible border remains.
        var style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE,
            style & ~(NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME | NativeMethods.WS_THICKFRAME));

        // Set WS_EX_NOREDIRECTIONBITMAP on GWL_EXSTYLE.
        // This tells DWM to composite the window from the DComp visual tree instead of
        // a GDI redirection bitmap, enabling per-pixel alpha for the entire window surface.
        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
            (exStyle | NativeMethods.WS_EX_NOREDIRECTIONBITMAP)
            & ~NativeMethods.WS_EX_WINDOWEDGE);

        // Windows 11: remove DWM border highlight and rounded corners.
        if (IsWindows11OrGreater())
        {
            var noBorder = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(
                _hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

            var noRound = NativeMethods.DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(
                _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(uint));
        }

        // Extend the DWM frame across the entire client area.
        // Without this, DWM leaves a thin artifact along the top edge even after all
        // other style bits are set correctly.
        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    private static bool IsWindows11OrGreater()
        => Environment.OSVersion.Version is { Major: > 10 } or { Major: 10, Build: >= 22000 };

    // Drag-to-move via pointer events on the XAML Grid.
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        _isDragging = true;
        _dragStart  = e.GetCurrentPoint(null).Position;
        _windowStartPos = AppWindow.Position;
        ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        var current = e.GetCurrentPoint(null).Position;
        var dx = (int)(current.X - _dragStart.X);
        var dy = (int)(current.Y - _dragStart.Y);
        AppWindow.Move(new PointInt32(_windowStartPos.X + dx, _windowStartPos.Y + dy));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }
}
