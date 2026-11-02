using DiscImageStudio.Localization;
using System.IO;
using DiscImageStudio.Core.Calibration;
using DiscImageStudio.Imaging;

namespace DiscImageStudio.Services;

public sealed record CalibrationPhotoContours(IReadOnlyList<CalibrationPoint> Outer,
    IReadOnlyList<CalibrationPoint> Hole);

/// <summary>Bounded photo decoding and perspective resampling; independent of the engraved target.</summary>
public static class CalibrationPhotoService
{
    public static Bgra32Image Load(string path, int maxDimension = 2000, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(L.T("请选择一张盘片照片。"), nameof(path));
        if (maxDimension is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxDimension), L.T("照片尺寸上限应为 64–4096 像素。"));
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 256L * 1024 * 1024) throw new ArgumentException(L.T("照片文件过大，请使用小于 256 MB 的图片。"));
        Bgra32Image decoded;
        try { decoded = ImageCodec.Decode(stream); }
        catch (NotSupportedException) { throw new ArgumentException(L.T("照片没有可读取的图像。")); }
        if (decoded.Width <= 0 || decoded.Height <= 0) throw new ArgumentException(L.T("照片尺寸无效。"));
        cancellationToken.ThrowIfCancellationRequested();
        // Longest side capped at maxDimension, aspect preserved; smaller photos keep their size.
        Bgra32Image image = ImageCodec.Fit(decoded, maxDimension, maxDimension);
        cancellationToken.ThrowIfCancellationRequested();
        return image;
    }

    public static CalibrationPhotoContours SuggestContours(Bgra32Image image, int count = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (count is < 5 or > 64) throw new ArgumentOutOfRangeException(nameof(count), L.T("每条初始轮廓应有 5–64 个点。"));
        if (image.Width < 16 || image.Height < 16) throw new ArgumentException(L.T("照片太小，无法输入轮廓。"));
        double cx = (image.Width - 1) / 2.0, cy = (image.Height - 1) / 2.0;
        double rx = image.Width * 0.43, ry = image.Height * 0.43;
        CalibrationPoint[] Make(double ratio) => Enumerable.Range(0, count).Select(index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            double angle = -Math.PI / 2 + 2 * Math.PI * index / count;
            return new CalibrationPoint(cx + rx * ratio * Math.Cos(angle), cy + ry * ratio * Math.Sin(angle));
        }).ToArray();
        return new(Array.AsReadOnly(Make(1)), Array.AsReadOnly(Make(0.125)));
    }

    public static Bgra32Image Rectify(Bgra32Image image, CalibrationPhotoTransform transform,
        double canvasOuterRadiusMm, int pixelSize = 1200, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(transform);
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(canvasOuterRadiusMm) || canvasOuterRadiusMm is <= 0 or > 150)
            throw new ArgumentOutOfRangeException(nameof(canvasOuterRadiusMm), L.T("矫正画布半径无效。"));
        if (pixelSize is < 32 or > 4096) throw new ArgumentOutOfRangeException(nameof(pixelSize), L.T("矫正图片尺寸应为 32–4096 像素。"));
        if (image.Width is < 1 or > 4096 || image.Height is < 1 or > 4096)
            throw new ArgumentException(L.T("请先按照片尺寸上限载入图片，再进行矫正。"));
        if (!transform.IsValidForDisc(canvasOuterRadiusMm)) throw new ArgumentException(L.T("照片变换在当前画布内退化，请重新矫正轮廓或缩小画布。"));
        int width = image.Width, height = image.Height;
        int sourceStride = checked(width * 4), stride = checked(pixelSize * 4);
        byte[] source = image.Pixels;
        cancellationToken.ThrowIfCancellationRequested();
        byte[] pixels = new byte[checked(stride * pixelSize)];
        double[] matrix = transform.DiscToSourceMatrix9;
        double step = 2 * canvasOuterRadiusMm / pixelSize;
        Parallel.For(0, pixelSize, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            double ym = (y + 0.5) * step - canvasOuterRadiusMm;
            for (int x = 0; x < pixelSize; x++)
            {
                double xm = (x + 0.5) * step - canvasOuterRadiusMm;
                double divisor = matrix[6] * xm + matrix[7] * ym + matrix[8];
                int destination = y * stride + x * 4;
                double sx = (matrix[0] * xm + matrix[1] * ym + matrix[2]) / divisor;
                double sy = (matrix[3] * xm + matrix[4] * ym + matrix[5]) / divisor;
                if (xm * xm + ym * ym > canvasOuterRadiusMm * canvasOuterRadiusMm
                    || !double.IsFinite(sx) || !double.IsFinite(sy) || sx < 0 || sy < 0 || sx > width - 1 || sy > height - 1)
                {
                    pixels[destination] = pixels[destination + 1] = pixels[destination + 2] = pixels[destination + 3] = 255;
                    continue;
                }
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
                double fx = sx - x0, fy = sy - y0;
                for (int channel = 0; channel < 4; channel++)
                {
                    double value = source[y0 * sourceStride + x0 * 4 + channel] * (1 - fx) * (1 - fy)
                        + source[y0 * sourceStride + x1 * 4 + channel] * fx * (1 - fy)
                        + source[y1 * sourceStride + x0 * 4 + channel] * (1 - fx) * fy
                        + source[y1 * sourceStride + x1 * 4 + channel] * fx * fy;
                    pixels[destination + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
                }
            }
        });
        cancellationToken.ThrowIfCancellationRequested();
        return new Bgra32Image(pixelSize, pixelSize, pixels);
    }
}
