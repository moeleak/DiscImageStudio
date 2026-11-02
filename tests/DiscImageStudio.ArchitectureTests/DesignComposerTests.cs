using System.IO;
using DiscImageStudio.Imaging;
using DiscImageStudio.Services;

internal static class DesignComposerTests
{
    private readonly record struct Rgba(byte R, byte G, byte B, byte A);

    /// <summary>The design page's composite must come out as the square, alpha-carrying
    /// picture the generation engines already accept: side = output size, layers painted
    /// bottom-up, and the mm→px mapping centred on the disc.</summary>
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"disc-composer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string layerPath = Path.Combine(directory, "layer.png");
            ImageCodec.SavePng(layerPath, Bgra32Image.CreateSolid(100, 60, 40, 40, 200, 200));

            Bgra32Image? layer = DesignComposer.DecodeFrozenImage(layerPath);
            Require(layer is not null, "layer decodes");
            Require(DesignComposer.DecodeFrozenImage(Path.Combine(directory, "missing.png")) is null,
                "an unreadable layer decodes to null");

            // One layer, scale 1: the fitted base spans 2 × canvasOuterRadius on its larger
            // side, so at translate 0 the image centre sits on the disc centre.
            var items = new List<DesignLayerRenderItem>
            {
                new(layer!, BaseWidthMm: 115.0, BaseHeightMm: 69.0, TranslateXMm: 0, TranslateYMm: 0,
                    RotationDeg: 0, Scale: 1.0),
            };
            string compositePath = Path.Combine(directory, "composite.png");
            DesignComposer.Render(items, 57.5, 512, compositePath);

            Bgra32Image composite = ImageCodec.Decode(compositePath);
            Require(composite.Width == 512 && composite.Height == 512, "composite is square at output size");
            Require(composite.Pixels.Where((_, index) => (index & 3) == 3).Any(alpha => alpha < 255), "composite keeps alpha");

            // Centre pixel: the layer covers it, so it must carry the layer colour.
            Rgba center = Sample(composite, 256, 256);
            Require(center.R > 150 && center.G < 100 && center.B < 100, "layer painted at disc centre");
            Require(Math.Abs(center.A - 200) <= 2, "straight layer alpha survives composition");
            Require(Sample(composite, 2, 2).A == 0, "uncovered corners stay transparent");

            // Rotation: 90° turns the wide footprint tall, so a point right of the centre
            // (inside the unrotated footprint, outside the rotated one) turns transparent
            // while the point above the centre stays covered.
            var rotated = new List<DesignLayerRenderItem>
            {
                new(layer!, 115.0, 69.0, 0, 0, 90, 1.0),
            };
            string rotatedPath = Path.Combine(directory, "rotated.png");
            DesignComposer.Render(rotated, 57.5, 512, rotatedPath);
            Bgra32Image rotatedBitmap = ImageCodec.Decode(rotatedPath);
            Require(Sample(rotatedBitmap, 456, 256).A < 32, "rotation moves the footprint");
            Require(Sample(rotatedBitmap, 256, 216).A > 150, "rotation keeps the covered centre");

            // Translation moves the footprint by millimetres from the disc centre.
            var shifted = new List<DesignLayerRenderItem> { new(layer!, 20.0, 20.0, 30, 0, 0, 1.0) };
            string shiftedPath = Path.Combine(directory, "shifted.png");
            DesignComposer.Render(shifted, 57.5, 512, shiftedPath);
            Bgra32Image shiftedBitmap = ImageCodec.Decode(shiftedPath);
            double pixelsPerMm = 512 / 115.0;
            Require(Sample(shiftedBitmap, (int)Math.Round(256 + 30 * pixelsPerMm), 256).A > 150
                && Sample(shiftedBitmap, 256, 256).A == 0, "translation is measured in disc millimetres");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine("design-composer: passed (square alpha composite, centre mapping, rotation, translation)");
    }

    private static Rgba Sample(Bgra32Image bitmap, int x, int y)
    {
        int offset = (y * bitmap.Width + x) * 4;
        return new(bitmap.Pixels[offset + 2], bitmap.Pixels[offset + 1], bitmap.Pixels[offset], bitmap.Pixels[offset + 3]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("DesignComposer: " + message);
        }
    }
}
