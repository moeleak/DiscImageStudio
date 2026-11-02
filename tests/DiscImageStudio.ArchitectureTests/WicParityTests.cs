#if WINDOWS
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiscImageStudio.Imaging;

/// <summary>Windows-only baseline: every engine used to decode through WIC
/// (BitmapFrame → FormatConvertedBitmap(Bgra32)). The portable codec must give identical
/// bytes for lossless formats and stay within a small tolerance for JPEG.</summary>
internal static class WicParityTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"wic-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const int width = 23, height = 17;
            byte[] bgra = new byte[width * height * 4];
            for (int index = 0; index < width * height; index++)
            {
                bgra[index * 4] = (byte)(index * 37);
                bgra[index * 4 + 1] = (byte)(index * 53 + 7);
                bgra[index * 4 + 2] = (byte)(index * 11 + 101);
                bgra[index * 4 + 3] = (byte)(index % 7 == 0 ? 0 : 255 - index);
            }

            byte[] gray = Enumerable.Range(0, width * height).Select(index => (byte)(index * 13)).ToArray();
            BitmapSource bgraSource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            BitmapSource graySource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, gray, width);
            BitmapSource rgb48 = new FormatConvertedBitmap(bgraSource, PixelFormats.Rgb48, null, 0);
            BitmapSource indexed = new FormatConvertedBitmap(bgraSource, PixelFormats.Indexed8,
                new BitmapPalette(bgraSource, 256), 0);
            BitmapSource opaque = new FormatConvertedBitmap(bgraSource, PixelFormats.Bgr24, null, 0);

            Compare(directory, "bgra.png", new PngBitmapEncoder(), bgraSource, tolerance: 0);
            Compare(directory, "gray.png", new PngBitmapEncoder(), graySource, tolerance: 0);
            Compare(directory, "rgb48.png", new PngBitmapEncoder(), rgb48, tolerance: 1);
            Compare(directory, "indexed.png", new PngBitmapEncoder(), indexed, tolerance: 0);
            Compare(directory, "opaque.bmp", new BmpBitmapEncoder(), opaque, tolerance: 0);
            Compare(directory, "indexed.gif", new GifBitmapEncoder(), indexed, tolerance: 0);
            Compare(directory, "opaque.tif", new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw }, opaque, tolerance: 0);
            Compare(directory, "opaque.jpg", new JpegBitmapEncoder { QualityLevel = 95 }, opaque, tolerance: 4);
            Console.WriteLine("wic-parity: passed (PNG 8/16-bit, palette, Gray8, BMP, GIF, TIFF exact; JPEG within ±4)");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Compare(string directory, string name, BitmapEncoder encoder, BitmapSource source, int tolerance)
    {
        string path = Path.Combine(directory, name);
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (FileStream output = File.Create(path)) encoder.Save(output);

        byte[] expected;
        int width, height;
        using (FileStream input = File.OpenRead(path))
        {
            BitmapFrame frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            FormatConvertedBitmap converted = new(frame, PixelFormats.Bgra32, null, 0);
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            expected = new byte[width * height * 4];
            converted.CopyPixels(expected, width * 4, 0);
        }

        Bgra32Image actual = ImageCodec.Decode(path);
        if (actual.Width != width || actual.Height != height)
            throw new InvalidOperationException($"WIC parity {name}: size {actual.Width}x{actual.Height} vs {width}x{height}.");
        int worst = 0;
        for (int index = 0; index < expected.Length; index++)
        {
            // WIC zeroes nothing, but a fully transparent pixel's colour is not observable in
            // formats without straight alpha; compare colour only where alpha is non-zero.
            if ((index & 3) != 3 && expected[index | 3] == 0 && actual.Pixels[index | 3] == 0) continue;
            worst = Math.Max(worst, Math.Abs(expected[index] - actual.Pixels[index]));
        }

        if (worst > tolerance)
            throw new InvalidOperationException($"WIC parity {name}: max channel difference {worst} exceeds {tolerance}.");
    }
}
#endif
