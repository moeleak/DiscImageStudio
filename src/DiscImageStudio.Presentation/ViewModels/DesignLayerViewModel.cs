using CommunityToolkit.Mvvm.ComponentModel;
using DiscImageStudio.Imaging;
using DiscImageStudio.Services;

namespace DiscImageStudio.ViewModels;

/// <summary>One layer of the design page: an image placed relative to the disc centre.
/// Translation is millimetres (+x right, +y down), rotation degrees clockwise, and scale a
/// factor on the layer's fitted base size (the size at which its larger dimension spans the
/// content ring's diameter at import time).</summary>
public partial class DesignLayerViewModel : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private double _translateXMm;
    [ObservableProperty] private double _translateYMm;
    [ObservableProperty] private double _rotationDeg;
    [ObservableProperty] private double _scale = 1.0;

    public Bgra32Image? Image { get; private set; }

    /// <summary>Small decode for the layer panel's list row.</summary>
    public Bgra32Image? Thumbnail { get; private set; }

    /// <summary>Physical size at scale 1.0, derived once from the image aspect and the
    /// project's content ring; rendering multiplies by <see cref="Scale"/>.</summary>
    public double BaseWidthMm { get; private set; }

    public double BaseHeightMm { get; private set; }

    public bool HasImage => Image is not null;

    /// <summary>Scale expressed as a percentage for the property panel.</summary>
    public double ScalePercent
    {
        get => Scale * 100.0;
        set => Scale = value / 100.0;
    }

    partial void OnScaleChanged(double value) => OnPropertyChanged(nameof(ScalePercent));

    /// <summary>Decodes the image and derives the base size. Returns false when the file
    /// cannot be read; the layer then renders as a placeholder box.</summary>
    public bool LoadImage(double contentOuterRadiusMm)
    {
        Image = DesignComposer.DecodeFrozenImage(ImagePath);
        if (Image is null)
        {
            return false;
        }

        double width = Image.Width;
        double height = Image.Height;
        if (width <= 0 || height <= 0)
        {
            Image = null;
            return false;
        }

        // Fit rule: the larger dimension spans the content ring's diameter.
        double diameterMm = 2.0 * contentOuterRadiusMm;
        if (width >= height)
        {
            BaseWidthMm = diameterMm;
            BaseHeightMm = diameterMm * height / width;
        }
        else
        {
            BaseHeightMm = diameterMm;
            BaseWidthMm = diameterMm * width / height;
        }

        if (Thumbnail is null)
        {
            Thumbnail = ImageCodec.Fit(Image, ThumbnailWidth, 0);
        }

        return true;
    }

    private const int ThumbnailWidth = 96;
}
