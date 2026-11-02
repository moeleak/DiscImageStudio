using System.IO;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using DiscImageStudio.Imaging;
using SkiaSharp;

/// <summary>The portable codec must keep the WIC contract the engines were written against:
/// straight alpha (RGB of transparent pixels retained), exact 8-bit values for lossless
/// formats, first frame only, DPI round trip, and NotSupportedException for bad input.</summary>
internal static class ImageCodecTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"image-codec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            TestStraightAlphaRoundTrip(directory);
            TestGray8RoundTripAndDpi(directory);
            TestTiff(directory);
            TestJpeg(directory);
            TestFit();
            TestRejectsGarbage(directory);
            Console.WriteLine("image-codec: passed (straight alpha, Gray8, DPI, TIFF, JPEG, downscale, invalid input)");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestStraightAlphaRoundTrip(string directory)
    {
        const int width = 7, height = 5;
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++)
        {
            pixels[index * 4] = (byte)(index * 37);
            pixels[index * 4 + 1] = (byte)(index * 53 + 7);
            pixels[index * 4 + 2] = (byte)(index * 11 + 101);
            pixels[index * 4 + 3] = (byte)(index % 5 == 0 ? 0 : index * 29);
        }

        string path = Path.Combine(directory, "alpha.png");
        ImageCodec.SavePng(path, new Bgra32Image(width, height, pixels));
        Bgra32Image decoded = ImageCodec.Decode(path);
        Require(decoded.Width == width && decoded.Height == height, "PNG dimensions survive");
        Require(decoded.Pixels.SequenceEqual(pixels), "straight BGRA, including RGB under alpha 0, survives a PNG round trip");

        string opaquePath = Path.Combine(directory, "opaque.png");
        byte[] opaque = Bgra32Image.CreateSolid(3, 2, 10, 20, 30).Pixels;
        ImageCodec.SavePng(opaquePath, new Bgra32Image(3, 2, opaque));
        Require(ImageCodec.Decode(opaquePath).Pixels.SequenceEqual(opaque), "opaque PNGs decode with alpha 255");
    }

    private static void TestGray8RoundTripAndDpi(string directory)
    {
        byte[] gray = Enumerable.Range(0, 6 * 4).Select(index => (byte)(index * 11)).ToArray();
        string path = Path.Combine(directory, "gray.png");
        ImageCodec.SavePng(path, new Gray8Image(6, 4, gray, 144, 120));
        Bgra32Image decoded = ImageCodec.Decode(path);
        Require(TestImages.ReadGray(decoded).SequenceEqual(gray), "Gray8 PNG values are exact");
        Require(Math.Abs(decoded.DpiX - 144) < 0.05 && Math.Abs(decoded.DpiY - 120) < 0.05, "PNG pHYs DPI round trip");
        Require(ImageCodec.ReadSize(path) == (6, 4), "ReadSize reports pixel dimensions");
    }

    private static void TestTiff(string directory)
    {
        string path = Path.Combine(directory, "rgba.tif");
        const int width = 4, height = 3;
        using (Tiff tiff = Tiff.Open(path, "w"))
        {
            tiff.SetField(TiffTag.IMAGEWIDTH, width);
            tiff.SetField(TiffTag.IMAGELENGTH, height);
            tiff.SetField(TiffTag.SAMPLESPERPIXEL, 4);
            tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
            tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
            tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tiff.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.UNASSALPHA });
            tiff.SetField(TiffTag.XRESOLUTION, 300f);
            tiff.SetField(TiffTag.YRESOLUTION, 300f);
            tiff.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);
            for (int y = 0; y < height; y++)
            {
                byte[] row = new byte[width * 4];
                for (int x = 0; x < width; x++)
                {
                    row[x * 4] = (byte)(x * 60);
                    row[x * 4 + 1] = (byte)(y * 90);
                    row[x * 4 + 2] = 200;
                    row[x * 4 + 3] = 255;
                }
                tiff.WriteScanline(row, y);
            }
        }

        Bgra32Image decoded = ImageCodec.Decode(path);
        Require(decoded.Width == width && decoded.Height == height, "TIFF dimensions");
        int offset = ((2 * width) + 3) * 4;
        Require(decoded.Pixels[offset] == 200 && decoded.Pixels[offset + 1] == 180
            && decoded.Pixels[offset + 2] == 180 && decoded.Pixels[offset + 3] == 255,
            "TIFF pixels decode top-left first as BGRA");
        Require(Math.Abs(decoded.DpiX - 300) < 0.01, "TIFF resolution");
    }

    private static void TestJpeg(string directory)
    {
        const int size = 16;
        byte[] pixels = Bgra32Image.CreateSolid(size, size, 40, 120, 220).Pixels;
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        byte[] jpeg;
        try
        {
            using SKPixmap pixmap = new(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque),
                handle.AddrOfPinnedObject(), size * 4);
            using SKData data = pixmap.Encode(new SKJpegEncoderOptions(95, SKJpegEncoderDownsample.Downsample444,
                SKJpegEncoderAlphaOption.Ignore))!;
            jpeg = data.ToArray();
        }
        finally
        {
            handle.Free();
        }

        string path = Path.Combine(directory, "solid.jpg");
        File.WriteAllBytes(path, jpeg);
        Bgra32Image decoded = ImageCodec.Decode(path);
        Require(decoded.Width == size && decoded.Pixels[3] == 255, "JPEG decodes opaque");
        Require(Math.Abs(decoded.Pixels[0] - 40) <= 3 && Math.Abs(decoded.Pixels[1] - 120) <= 3
            && Math.Abs(decoded.Pixels[2] - 220) <= 3, "JPEG colour stays within lossy tolerance");
    }

    private static void TestFit()
    {
        Bgra32Image large = Bgra32Image.CreateSolid(400, 100, 0, 0, 255);
        Bgra32Image fitted = ImageCodec.Fit(large, 96, 0);
        Require(fitted.Width == 96 && fitted.Height == 24, "width-limited downscale keeps aspect");
        Require(fitted.Pixels[2] >= 250 && fitted.Pixels[3] == 255, "downscale keeps a solid colour");
        Require(ImageCodec.Fit(large, 1000, 1000) == large, "images are never enlarged");
        Bgra32Image tall = ImageCodec.Fit(Bgra32Image.CreateSolid(30, 300, 1, 2, 3), 64, 64);
        Require(tall.Width == 6 && tall.Height == 64, "both limits apply");
    }

    private static void TestRejectsGarbage(string directory)
    {
        string path = Path.Combine(directory, "garbage.png");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        bool rejected = false;
        try { ImageCodec.Decode(path); }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected, "undecodable files raise NotSupportedException");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Image codec: " + message);
    }
}
