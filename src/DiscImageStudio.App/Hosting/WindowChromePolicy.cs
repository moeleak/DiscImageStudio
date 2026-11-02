using Avalonia.Controls;

namespace DiscImageStudio.Hosting;

/// <summary>
/// One place that decides how every window is framed:
/// Windows — borderless caption drawn by the app (title row, app menu, min/max/close), with
///   the system resize border kept;
/// macOS — content extended under the native title bar, traffic lights left in place;
/// Linux — the window manager draws decorations (server-side under X11, and XWayland), the
///   app's title row becomes an in-window header without caption buttons.
/// </summary>
public static class WindowChromePolicy
{
    /// <summary>The app draws minimize/maximize/close itself.</summary>
    public static bool DrawsCaptionButtons => OperatingSystem.IsWindows();

    /// <summary>Dragging the title row moves the window (no system caption to grab).</summary>
    public static bool TitleBarMovesWindow => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>Space reserved at the left of the title row (macOS traffic lights).</summary>
    public static double LeadingInset => OperatingSystem.IsMacOS() ? 70 : 0;

    public static void Apply(Window window)
    {
        if (OperatingSystem.IsWindows())
        {
            window.WindowDecorations = WindowDecorations.BorderOnly;
        }
        else if (OperatingSystem.IsMacOS())
        {
            window.WindowDecorations = WindowDecorations.Full;
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaTitleBarHeightHint = 32;
        }
        else
        {
            window.WindowDecorations = WindowDecorations.Full;
        }
    }
}
