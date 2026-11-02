namespace DiscImageStudio.Imaging;

/// <summary>Common shape of the decoded rasters; UI hosts convert either kind for display.</summary>
public interface IRasterImage
{
    int Width { get; }

    int Height { get; }

    double DpiX { get; }

    double DpiY { get; }
}

/// <summary>
/// Tightly packed, straight (non-premultiplied) alpha BGRA pixels. This is the portable
/// replacement for WPF's Bgra32 <c>BitmapSource</c>: colour values of fully transparent
/// pixels are retained exactly as decoded.
/// </summary>
public sealed class Bgra32Image : IRasterImage
{
    public Bgra32Image(int width, int height, byte[] pixels, double dpiX = ImageResolution.DefaultDpi,
        double dpiY = ImageResolution.DefaultDpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.LongLength != (long)width * height * 4)
        {
            throw new ArgumentException("The BGRA buffer must contain exactly width × height × 4 bytes.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
        DpiX = ImageResolution.Valid(dpiX);
        DpiY = ImageResolution.Valid(dpiY);
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    /// <summary>Row-major BGRA bytes, <see cref="Stride"/> bytes per row.</summary>
    public byte[] Pixels { get; }

    public double DpiX { get; }

    public double DpiY { get; }

    public static Bgra32Image CreateSolid(int width, int height, byte blue, byte green, byte red, byte alpha = 255)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }

        return new Bgra32Image(width, height, pixels);
    }
}

/// <summary>Tightly packed 8-bit grayscale pixels (one byte per pixel, no alpha).</summary>
public sealed class Gray8Image : IRasterImage
{
    public Gray8Image(int width, int height, byte[] pixels, double dpiX = ImageResolution.DefaultDpi,
        double dpiY = ImageResolution.DefaultDpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.LongLength != (long)width * height)
        {
            throw new ArgumentException("The Gray8 buffer must contain exactly width × height bytes.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
        DpiX = ImageResolution.Valid(dpiX);
        DpiY = ImageResolution.Valid(dpiY);
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width;

    public byte[] Pixels { get; }

    public double DpiX { get; }

    public double DpiY { get; }

    /// <summary>Expands to opaque BGRA (R = G = B = gray).</summary>
    public Bgra32Image ToBgra32()
    {
        byte[] bgra = new byte[checked(Pixels.Length * 4)];
        for (int index = 0, offset = 0; index < Pixels.Length; index++, offset += 4)
        {
            byte gray = Pixels[index];
            bgra[offset] = gray;
            bgra[offset + 1] = gray;
            bgra[offset + 2] = gray;
            bgra[offset + 3] = 255;
        }

        return new Bgra32Image(Width, Height, bgra, DpiX, DpiY);
    }
}
