using System.IO;
using DiscImageStudio.BluRay;
using DiscImageStudio.Core;
using DiscImageStudio.Imaging;

namespace DiscImageStudio.Services;

/// <summary>Loads an image as Gray8 without choosing a Blu-ray intensity-to-byte mapping.</summary>
public static class BluRayImageLoader
{
    private const long MaxFileBytes = 256L * 1024 * 1024;
    private const long MaxPixels = 64L * 1024 * 1024;

    /// <summary>Uses encoded-space Rec. 709 weights and composites straight alpha over
    /// white. Without a tone profile the conversion preserves source dimensions and performs
    /// no thresholding, dithering, ICC conversion or optical-reflectance calibration.
    /// A tone profile is applied here, at source resolution: BD payload positions are far
    /// denser than any practical raster, so dithering per byte is neither needed nor
    /// reproducible.</summary>
    public static BluRayGray8Raster Load(string path, CancellationToken cancellationToken = default)
        => Load(path, tone: null, cancellationToken);

    public static BluRayGray8Raster Load(string path, ToneProfile? tone,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSource(path, cancellationToken);
        Bgra32Image decoded = ImageCodec.Decode(path);
        int width = decoded.Width, height = decoded.Height;
        byte[] bgra = decoded.Pixels;
        byte[] gray = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int pixel = y * width + x;
                int offset = pixel * 4;
                int weighted = 2126 * bgra[offset + 2] + 7152 * bgra[offset + 1] + 722 * bgra[offset];
                int alpha = bgra[offset + 3];
                gray[pixel] = (byte)((weighted * alpha + 2_550_000 * (255 - alpha) + 1_275_000) / 2_550_000);
            }
        }

        if (tone is { IsLegacyBinary: false } effective)
        {
            for (int pixel = 0; pixel < gray.Length; pixel++)
            {
                if ((pixel & 0xFFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                gray[pixel] = effective.UseWhite(gray[pixel], pixel) ? (byte)255 : (byte)0;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(width, height, gray);
    }

    /// <summary>Checks file size and first-frame dimensions without copying or decoding
    /// the pixel buffer, so preprocessing cannot bypass the source resource limits.</summary>
    public static void ValidateSource(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo file = new(Path.GetFullPath(path));
        if (file.Length > MaxFileBytes)
            throw new ArgumentException("The source image must be no larger than 256 MiB.", nameof(path));
        (int width, int height) = ImageCodec.ReadSize(file.FullName);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            throw new ArgumentException("The source image must contain between 1 and 67,108,864 pixels.", nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static BluRayImagePayloadMapping CreateMapping(string path, BluRayDiscParameters disc,
        IBluRayLuminanceMapping mapping, CancellationToken cancellationToken = default,
        ToneProfile? tone = null)
    {
        // Validate the unresolved strategy before loading any source file.
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(disc);
        disc.Validate();
        return new(disc, Load(path, tone, cancellationToken), mapping);
    }
}
