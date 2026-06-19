# WinUI 3 Transparent Window Repro

Minimal standalone repro of all the DWM/Win32 workarounds required to achieve a transparent, borderless, always-on-top overlay window in WinUI 3 (Windows App SDK 2.2.0, .NET 10).

The goal is a window with **per-pixel alpha** — the desktop behind it is fully visible and clickable through transparent areas, with no white bleeding or border artifacts.

## Run

Requires [Windows App SDK 2.2](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) installed.

```
dotnet run
```

A small blue circle appears floating on the desktop. Drag it to move.

## The Problem

WinUI 3 does not provide a first-class API for transparent overlay windows. Achieving per-pixel alpha requires 9 separate Win32/DWM workarounds. Without each one, a specific artifact appears:

| # | Workaround | Artifact if omitted |
|---|---|---|
| 1 | `ShowWindow(SW_HIDE)` before `InitializeComponent` | Black/white flash during startup |
| 2 | Set `WS_EX_NOREDIRECTIONBITMAP` before `InitializeComponent` | White GDI bitmap bleeds through transparent areas on SDR displays |
| 3 | `SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false)` | Thin white border on Windows 10 22H2 (`WS_DLGFRAME` injected by DWM) |
| 4 | `ExtendsContentIntoTitleBar = true` | White stripe at top of window (GDI background exposed behind XAML island) |
| 5 | Re-apply styles after every `AppWindow.*` call | `WS_EX_NOREDIRECTIONBITMAP` silently cleared by `SetPresenter` / `ExtendsContentIntoTitleBar` |
| 6 | Defer `SystemBackdrop` assignment to first `Activated` event | Null vtable crash (`0xC0000005`) — races against DWM/WarpPal initialisation |
| 7 | Re-apply styles after setting `SystemBackdrop` | Backdrop controller resets `GWL_EXSTYLE`, clearing `WS_EX_NOREDIRECTIONBITMAP` |
| 8 | `EnumChildWindows` to patch XAML island child HWNDs | White rectangles behind semi-transparent XAML (each child HWND paints its own white fill) |
| 9 | Native subclass: suppress `WM_ERASEBKGND`, re-apply on `WM_DISPLAYCHANGE` | White flash on every invalidation; transparency breaks on monitor connect/disconnect |

All workarounds are implemented in [`MainWindow.xaml.cs`](MainWindow.xaml.cs) with comments explaining why each one is necessary.

## Files

| File | Purpose |
|---|---|
| `MainWindow.xaml.cs` | All 9 DWM workarounds |
| `NativeMethods.cs` | P/Invoke declarations (user32, gdi32, dwmapi, comctl32) |
| `MainWindow.xaml` | Minimal XAML — transparent Grid with a coloured circle |
| `App.xaml` / `App.xaml.cs` | Minimal app entry point |
| `TransparentWindowRepro.csproj` | Unpackaged debug config (`dotnet run` works without MSIX) |

## Environment

- Windows App SDK 2.2.0
- WinUI 3 / .NET 10
- Target: `net10.0-windows10.0.19041.0`
- Min OS: Windows 10 build 17763
