using DiscImageStudio.Core;
using DiscImageStudio.Core.Calibration;
using DiscImageStudio.Imaging;

namespace DiscImageStudio.Services;

internal static class CalibrationPatternRenderer
{
    internal static Gray8Image Render(CalibrationTarget target, double canvasRadius, int size,
        CalibrationParameters? actual = null, double rotation = 0, CancellationToken cancellationToken = default)
    {
        Func<CalibrationPoint, CalibrationPoint?>? inverse = actual is null
            ? null : target.CreateInverseMapping(actual, rotation);
        var bounds = actual ?? target.Parameters;
        // Canvas size controls the physical background only. Target.Sample owns
        // the source-image scale, which is captured in its generation parameters.
        byte[] pixels = new byte[checked(size * size)];
        double step = 2 * canvasRadius / size;
        Parallel.For(0, size, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            double ym = (y + 0.5) * step - canvasRadius;
            for (int x = 0; x < size; x++)
            {
                double xm = (x + 0.5) * step - canvasRadius;
                double r2 = xm * xm + ym * ym;
                byte value = 255;
                if (r2 >= bounds.InnerRadiusMm * bounds.InnerRadiusMm - 1e-10 && r2 <= bounds.OuterRadiusMm * bounds.OuterRadiusMm + 1e-10)
                {
                    CalibrationPoint? p = inverse is null ? new(xm, ym) : inverse(new(xm, ym));
                    if (p is not null)
                    {
                        byte sampled = target.Sample(p.X, p.Y);
                        value = BinaryGrayscale.Quantize(sampled);
                    }
                }
                pixels[y * size + x] = value;
            }
        });
        return new Gray8Image(size, size, pixels);
    }

    /// <summary>Original PNG pixels cropped in the ordinary encoder's image coordinates.</summary>
    internal static Gray8Image RenderEncodingImage(CalibrationTarget target, CancellationToken cancellationToken = default)
    {
        const int size = CalibrationReferencePattern.PixelSize;
        byte[] pixels = CalibrationReferencePattern.ReadGrayPixels();
        CalibrationParameters parameters = target.Parameters;
        double inner2 = target.DrawingInnerRadiusMm * target.DrawingInnerRadiusMm;
        double outer2 = target.DrawingOuterRadiusMm * target.DrawingOuterRadiusMm;
        Parallel.For(0, size, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            for (int x = 0; x < size; x++)
            {
                CalibrationPoint p = CalibrationReferencePattern.PixelToDisc(x, y, parameters.OuterRadiusMm, parameters.Kind);
                double r2 = p.X * p.X + p.Y * p.Y;
                if (r2 < inner2 - 1e-10 || r2 > outer2 + 1e-10) pixels[y * size + x] = 255;
            }
        });
        return new Gray8Image(size, size, pixels);
    }

    internal static void SavePng(Gray8Image image, string path) => ImageCodec.SavePng(path, image);
}
