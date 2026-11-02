using System.Runtime.InteropServices;
using SkiaSharp;

namespace DiscImageStudio.Imaging;

/// <summary>
/// The single cross-platform image decode/encode entry point (replaces WPF/WIC).
/// Decoding returns the first frame as straight-alpha BGRA without colour management
/// or EXIF orientation, the same contract the WIC <c>FormatConvertedBitmap(Bgra32)</c>
/// path had. PNG, JPEG, BMP, GIF, WebP and ICO decode through Skia; TIFF through LibTiff.NET.
/// Every failure to recognise or decode an image surfaces as <see cref="NotSupportedException"/>.
/// </summary>
public static class ImageCodec
{
    /// <summary>Extensions offered by file pickers (lower case, with dot).</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    /// <summary>Upper bound on a single decoded image, matching the 256 MB photo guard.</summary>
    private const long MaxEncodedBytes = 1L << 30;

    /// <summary>Decodes an image file. When <paramref name="maxWidth"/> or
    /// <paramref name="maxHeight"/> is positive, larger images are downscaled once (aspect
    /// preserved) so both limits hold; smaller images are never enlarged.</summary>
    public static Bgra32Image Decode(string path, int maxWidth = 0, int maxHeight = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Decode(stream, maxWidth, maxHeight);
    }

    public static Bgra32Image Decode(Stream stream, int maxWidth = 0, int maxHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] data;
        if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment))
        {
            data = segment.Slice((int)memory.Position).ToArray();
        }
        else
        {
            if (stream.CanSeek && stream.Length - stream.Position > MaxEncodedBytes)
            {
                throw new NotSupportedException("The image file is too large to decode.");
            }

            using MemoryStream copy = new();
            stream.CopyTo(copy);
            data = copy.ToArray();
        }

        return Decode(data, maxWidth, maxHeight);
    }

    public static Bgra32Image Decode(byte[] data, int maxWidth = 0, int maxHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxHeight);
        Bgra32Image image = TiffImageDecoder.IsTiff(data)
            ? TiffImageDecoder.Decode(data)
            : DecodeWithSkia(data);
        return Fit(image, maxWidth, maxHeight);
    }

    /// <summary>Reads only the pixel dimensions (first frame) without decoding pixels.</summary>
    public static (int Width, int Height) ReadSize(string path)
    {
        byte[] data = File.ReadAllBytes(Path.GetFullPath(path));
        if (TiffImageDecoder.IsTiff(data))
        {
            return TiffImageDecoder.ReadSize(data);
        }

        using SKData skData = SKData.CreateCopy(data);
        using SKCodec? codec = SKCodec.Create(skData)
            ?? throw new NotSupportedException("The file is not a supported image format.");
        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>Writes straight-alpha BGRA as an RGBA PNG (transparency preserved).</summary>
    public static void SavePng(string path, Bgra32Image image) => WriteAtomically(path, EncodePng(image));

    /// <summary>Writes an 8-bit grayscale PNG.</summary>
    public static void SavePng(string path, Gray8Image image) => WriteAtomically(path, EncodePng(image));

    public static byte[] EncodePng(Bgra32Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        SKImageInfo info = new(image.Width, image.Height, SKColorType.Bgra8888,
            IsOpaque(image.Pixels) ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
        return Encode(info, image.Pixels, image.Stride, image.DpiX, image.DpiY);
    }

    public static byte[] EncodePng(Gray8Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        SKImageInfo info = new(image.Width, image.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        return Encode(info, image.Pixels, image.Stride, image.DpiX, image.DpiY);
    }

    /// <summary>Downscales (never enlarges) so the result fits within the limits; zero means unlimited.</summary>
    public static Bgra32Image Fit(Bgra32Image image, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        double scale = 1.0;
        if (maxWidth > 0 && image.Width > maxWidth) scale = Math.Min(scale, maxWidth / (double)image.Width);
        if (maxHeight > 0 && image.Height > maxHeight) scale = Math.Min(scale, maxHeight / (double)image.Height);
        if (scale >= 1.0)
        {
            return image;
        }

        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        if (maxWidth > 0) width = Math.Min(width, maxWidth);
        if (maxHeight > 0) height = Math.Min(height, maxHeight);
        return Resize(image, width, height);
    }

    /// <summary>High-quality resample. Downscaling halves progressively (box-filter quality)
    /// before a final Mitchell cubic pass; filtering happens in premultiplied space so
    /// transparent pixels do not bleed colour.</summary>
    public static Bgra32Image Resize(Bgra32Image image, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width == image.Width && height == image.Height)
        {
            return image;
        }

        SKBitmap current = ToPremulBitmap(image);
        try
        {
            while (current.Width >= width * 2 && current.Height >= height * 2)
            {
                SKBitmap half = new(new SKImageInfo(current.Width / 2, current.Height / 2,
                    SKColorType.Bgra8888, SKAlphaType.Premul));
                current.ScalePixels(half, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                current.Dispose();
                current = half;
            }

            using SKBitmap target = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            current.ScalePixels(target, new SKSamplingOptions(SKCubicResampler.Mitchell));
            byte[] pixels = new byte[checked(width * height * 4)];
            SKImageInfo straight = new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                if (!target.PeekPixels().ReadPixels(straight, handle.AddrOfPinnedObject(), width * 4))
                {
                    throw new InvalidOperationException("Skia could not convert resampled pixels.");
                }
            }
            finally
            {
                handle.Free();
            }

            return new Bgra32Image(width, height, pixels, image.DpiX, image.DpiY);
        }
        finally
        {
            current.Dispose();
        }
    }

    private static Bgra32Image DecodeWithSkia(byte[] data)
    {
        (double dpiX, double dpiY) = ImageResolution.Read(data);
        using SKData skData = SKData.CreateCopy(data);
        using SKCodec? codec = SKCodec.Create(skData)
            ?? throw new NotSupportedException("The file is not a supported image format.");
        int width = codec.Info.Width;
        int height = codec.Info.Height;
        if (width <= 0 || height <= 0)
        {
            throw new NotSupportedException("The image has no pixels.");
        }

        // A null colour space means "no colour transform": bytes stay as encoded, like WIC.
        SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        byte[] pixels = new byte[checked((long)width * height * 4)];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            SKCodecResult result = codec.GetPixels(info, handle.AddrOfPinnedObject(), width * 4,
                new SKCodecOptions(0));
            if (result != SKCodecResult.Success)
            {
                throw new NotSupportedException($"The image could not be decoded ({result}).");
            }
        }
        finally
        {
            handle.Free();
        }

        return new Bgra32Image(width, height, pixels, dpiX, dpiY);
    }

    private static SKBitmap ToPremulBitmap(Bgra32Image image)
    {
        SKBitmap bitmap = new(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        SKImageInfo straight = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        GCHandle handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            using SKPixmap source = new(straight, handle.AddrOfPinnedObject(), image.Stride);
            if (!source.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes))
            {
                bitmap.Dispose();
                throw new InvalidOperationException("Skia could not premultiply the image.");
            }
        }
        finally
        {
            handle.Free();
        }

        return bitmap;
    }

    private static byte[] Encode(SKImageInfo info, byte[] pixels, int stride, double dpiX, double dpiY)
    {
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using SKPixmap pixmap = new(info, handle.AddrOfPinnedObject(), stride);
            using SKData? data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6))
                ?? throw new InvalidOperationException("Skia could not encode the PNG.");
            return ImageResolution.WithPngResolution(data.AsSpan(), dpiX, dpiY);
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool IsOpaque(ReadOnlySpan<byte> bgra)
    {
        for (int offset = 3; offset < bgra.Length; offset += 4)
        {
            if (bgra[offset] != 255) return false;
        }

        return true;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
