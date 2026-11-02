using System.IO;
using System.Runtime.InteropServices;
using DiscImageStudio.Imaging;
using SkiaSharp;

namespace DiscImageStudio.Services;

/// <summary>One layer flattened for rendering: physical placement in millimetres relative to
/// the disc centre plus the decoded bitmap. <paramref name="BaseWidthMm"/>/
/// <paramref name="BaseHeightMm"/> are the layer's physical size at scale 1.</summary>
public sealed record DesignLayerRenderItem(
    Bgra32Image Image,
    double BaseWidthMm,
    double BaseHeightMm,
    double TranslateXMm,
    double TranslateYMm,
    double RotationDeg,
    double Scale);

/// <summary>Renders the layer stack into the square source picture the generation pipeline
/// consumes: the square's centre is the disc centre and its side spans
/// 2 × <c>canvasOuterRadiusMm</c>. Rendering uses Skia and may run on any thread.</summary>
internal static class DesignComposer
{
    /// <summary>Upper bound on the decoded side of a layer image; anything larger is
    /// downscaled once at load, which stays invisible at the 8192 export size.</summary>
    internal const int MaxDecodeWidth = 4096;

    public static void Render(
        IReadOnlyList<DesignLayerRenderItem> layers,
        double canvasOuterRadiusMm,
        int outputSize,
        string outputPath)
    {
        if (canvasOuterRadiusMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasOuterRadiusMm));
        }

        double pixelsPerMm = outputSize / (2.0 * canvasOuterRadiusMm);
        double center = outputSize / 2.0;

        SKImageInfo surfaceInfo = new(outputSize, outputSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(surfaceInfo)
            ?? throw new InvalidOperationException("Skia could not allocate the design composite surface.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using SKPaint paint = new() { IsAntialias = true };
        // The layer list is ordered topmost-first, so painting runs bottom-up.
        for (int index = layers.Count - 1; index >= 0; index--)
        {
            DesignLayerRenderItem item = layers[index];
            double widthPixels = item.BaseWidthMm * item.Scale * pixelsPerMm;
            double heightPixels = item.BaseHeightMm * item.Scale * pixelsPerMm;
            if (widthPixels <= 0 || heightPixels <= 0)
            {
                continue;
            }

            using SKImage image = ToSkImage(item.Image);
            // Same order as the former WPF TransformGroup: rotate about the layer centre,
            // then translate so that centre lands on its physical position.
            canvas.Save();
            canvas.Translate(
                (float)(center + item.TranslateXMm * pixelsPerMm - widthPixels / 2),
                (float)(center + item.TranslateYMm * pixelsPerMm - heightPixels / 2));
            canvas.RotateDegrees((float)item.RotationDeg, (float)(widthPixels / 2), (float)(heightPixels / 2));
            double scale = Math.Min(widthPixels / item.Image.Width, heightPixels / item.Image.Height);
            SKSamplingOptions sampling = scale < 0.5
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKCubicResampler.Mitchell);
            canvas.DrawImage(image, SKRect.Create(0, 0, (float)widthPixels, (float)heightPixels), sampling, paint);
            canvas.Restore();
        }

        canvas.Flush();
        byte[] pixels = new byte[checked(outputSize * outputSize * 4)];
        SKImageInfo straight = new(outputSize, outputSize, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            if (!surface.ReadPixels(straight, handle.AddrOfPinnedObject(), outputSize * 4, 0, 0))
            {
                throw new InvalidOperationException("Skia could not read the design composite.");
            }
        }
        finally
        {
            handle.Free();
        }

        ImageCodec.SavePng(outputPath, new Bgra32Image(outputSize, outputSize, pixels));
    }

    /// <summary>Decodes a layer image, capped at <see cref="MaxDecodeWidth"/> pixels wide.
    /// Any decode failure returns null.</summary>
    public static Bgra32Image? DecodeFrozenImage(string path)
    {
        try
        {
            return ImageCodec.Decode(path, maxWidth: MaxDecodeWidth);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException
                                              or OutOfMemoryException
                                              or ArgumentException)
        {
            return null;
        }
    }

    private static SKImage ToSkImage(Bgra32Image image)
    {
        SKImageInfo info = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        GCHandle handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            return SKImage.FromPixelCopy(info, handle.AddrOfPinnedObject(), image.Stride)
                ?? throw new InvalidOperationException("Skia could not wrap a layer image.");
        }
        finally
        {
            handle.Free();
        }
    }
}
