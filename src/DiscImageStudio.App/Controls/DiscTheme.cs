using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace DiscImageStudio.Controls;

/// <summary>Theme-token lookups for code-drawn controls. Values are read for the current
/// theme variant at draw time, so a palette switch only needs a redraw.</summary>
internal static class ThemeValues
{
    public static object? Find(string key)
    {
        Application? application = Application.Current;
        if (application is null) return null;
        ThemeVariant variant = application.ActualThemeVariant;
        return application.TryGetResource(key, variant, out object? value) ? value : null;
    }

    public static Color Color(string key, Color fallback) => Find(key) switch
    {
        Color color => color,
        ISolidColorBrush brush => brush.Color,
        _ => fallback,
    };

    public static IBrush Brush(string key, IBrush fallback) => Find(key) as IBrush ?? fallback;

    public static double Double(string key, double fallback) => Find(key) is double value ? value : fallback;

    public static Color WithAlpha(Color color, byte alpha) => Avalonia.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
}

/// <summary>The disc palette shared by the annulus preview, design canvas and calibration canvas.</summary>
internal static class DiscTheme
{
    public static IBrush Backdrop => Resolve("DiscCanvasBackdropBrush", 0x0D, 0x0D, 0x0F);
    public static IBrush DiscBody => Resolve("DiscBodyBrush", 0x1C, 0x1C, 0x20);
    public static IBrush Hub => Resolve("DiscHubBrush", 0x0D, 0x0D, 0x0F);
    public static IBrush Ring => Resolve("DiscRingBrush", 0x30, 0x33, 0x85, 0xFF);
    public static IBrush Guide => Resolve("DiscGuideBrush", 0x55, 0xA1, 0xA1, 0xAA);
    public static IBrush Mask => Resolve("DiscMaskBrush", 0x88, 0x00, 0x00, 0x00);
    public static IBrush Selection => Resolve("DiscSelectionBrush", 0x33, 0x85, 0xFF);
    public static IBrush MissingImage => Resolve("DangerBrush", 0xF3, 0x12, 0x60);
    public static IBrush PhotoBackdrop => Resolve("CalibrationPhotoBackdropBrush", 0x13, 0x15, 0x1A);
    public static IBrush Caption => Resolve("CalibrationCaptionBrush", 0xDC, 0x13, 0x15, 0x1A);
    public static IBrush Edge => Resolve("CalibrationEdgeBrush", 0x74, 0xC0, 0xFF);
    public static IBrush HubOutline => Resolve("CalibrationHubOutlineBrush", 0xFF, 0xA5, 0x00);
    public static IBrush Trace => Resolve("CalibrationTraceBrush", 0x00, 0xFF, 0xFF);
    public static IBrush Prediction => Resolve("CalibrationPredictionBrush", 0xFF, 0x8C, 0x00);
    public static IBrush Outlier => Resolve("DangerBrush", 0xF3, 0x12, 0x60);
    public static IBrush Handle => Resolve("CalibrationHandleBrush", 0xD3, 0xD3, 0xD3);
    public static IBrush Outline => Resolve("CalibrationOutlineBrush", 0x00, 0x00, 0x00);
    public static IBrush TextMuted => Resolve("TextMutedBrush", 0x8C, 0x8C, 0x92);
    public static IBrush TextPrimary => Resolve("TextPrimaryBrush", 0xFA, 0xFA, 0xFA);

    public static FontFamily Font => ThemeValues.Find("AppFontFamily") as FontFamily ?? FontFamily.Default;

    public static Typeface Typeface => new(Font);

    private static IBrush Resolve(string key, byte r, byte g, byte b) => Resolve(key, 0xFF, r, g, b);

    private static IBrush Resolve(string key, byte a, byte r, byte g, byte b)
        => ThemeValues.Find(key) as IBrush ?? new SolidColorBrush(Color.FromArgb(a, r, g, b));
}
