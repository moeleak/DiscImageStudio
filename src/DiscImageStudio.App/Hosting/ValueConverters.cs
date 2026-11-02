using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using DiscImageStudio.Imaging;

namespace DiscImageStudio.Hosting;

/// <summary>Shared one-way converters for the Avalonia views.</summary>
public static class ValueConverters
{
    /// <summary>Any value to its invariant string (enum states for style selectors).</summary>
    public static readonly IValueConverter ToStringValue =
        new FuncValueConverter<object?, string?>(value => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture));

    /// <summary>The view models' portable rasters to an Avalonia bitmap (cached per raster).</summary>
    public static readonly IValueConverter RasterImage =
        new FuncValueConverter<IRasterImage?, Bitmap?>(AvaloniaBitmaps.ToBitmap);

    /// <summary><see cref="AppIcon"/> to the Lucide kind name.</summary>
    public static readonly IValueConverter AppIconKind =
        new FuncValueConverter<AppIcon, string>(icon => icon.ToString());

    public static readonly IValueConverter IsZero =
        new FuncValueConverter<int, bool>(count => count == 0);
}
