using System.IO;
using DiscImageStudio.Imaging;

internal static class FourLevelDitherTests
{
    internal static void Run()
    {
        TestPaletteIsUnchanged();
        TestSerpentineFixture();
        TestToneConservation();
        TestColorAndTransparency();
        TestSmallImagesAndCancellation();
        TestBitmapAndPngRoundTrip();
        Console.WriteLine("four-level-dither: passed (serpentine diffusion, tone, alpha, palette, cancellation and PNG)");
    }

    private static void TestPaletteIsUnchanged()
    {
        byte[] gray = Enumerable.Range(0, 35).Select(index => (byte)((index % 4) * 85)).ToArray();
        byte[] result = FourLevelDither.ProcessPixels(ToBgra(gray), 7, 5, CancellationToken.None);
        Require(result.SequenceEqual(gray), "already quantized images retain every pixel");
    }

    private static void TestSerpentineFixture()
    {
        byte[] input =
        [
            20, 50, 100, 150, 220,
            30, 80, 130, 180, 230,
            40, 90, 140, 190, 240,
            10, 60, 110, 160, 210,
        ];
        // Fixed Floyd-Steinberg golden fixture: even rows scan right, odd rows
        // scan left and mirror all four diffusion weights. A forward-only scan
        // differs at multiple pixels, including the middle pixel of row two.
        byte[] expected =
        [
            0, 85, 85, 170, 170,
            0, 85, 170, 170, 255,
            85, 85, 85, 170, 255,
            0, 0, 170, 170, 170,
        ];
        byte[] result = FourLevelDither.ProcessPixels(ToBgra(input), 5, 4, CancellationToken.None);
        Require(result.SequenceEqual(expected), "diffusion mirrors on alternate rows with the expected edge handling");
    }

    private static void TestToneConservation()
    {
        const int width = 129;
        const int height = 127;
        byte[] flat = Enumerable.Repeat((byte)128, width * height).ToArray();
        byte[] flatResult = FourLevelDither.ProcessPixels(ToBgra(flat), width, height, CancellationToken.None);
        RequirePalette(flatResult, "flat midgray");
        Require(flatResult.Contains((byte)85) && flatResult.Contains((byte)170),
            "flat midgray uses a spatial mixture of neighboring levels");
        Require(Math.Abs(flatResult.Average(value => (double)value) - 128) < 1.0,
            "flat midgray preserves average tone instead of rounding the entire image to one level");

        byte[] ramp = Enumerable.Range(0, 256 * 17).Select(index => (byte)(index % 256)).ToArray();
        byte[] rampResult = FourLevelDither.ProcessPixels(ToBgra(ramp), 256, 17, CancellationToken.None);
        RequirePalette(rampResult, "grayscale ramp");
        Require(Math.Abs(rampResult.Average(value => (double)value) - 127.5) < 1.0,
            "a full grayscale ramp conserves average tone");
        Require(rampResult.Distinct().Count() == 4, "the full ramp uses all four output levels");
    }

    private static void TestColorAndTransparency()
    {
        foreach ((byte blue, byte green, byte red, byte alpha, byte expected) in new (byte, byte, byte, byte, byte)[]
        {
            (0, 0, 255, 255, 85),
            (0, 255, 0, 255, 170),
            (255, 0, 0, 255, 0),
            (0, 0, 0, 0, 255),
            (255, 17, 91, 0, 255),
            (0, 0, 0, 128, 85),
            (0, 0, 0, 127, 170),
            (0, 0, 255, 128, 170),
        })
        {
            byte[] result = FourLevelDither.ProcessPixels([blue, green, red, alpha], 1, 1, CancellationToken.None);
            Require(result.Length == 1 && result[0] == expected,
                $"BGRA ({blue}, {green}, {red}, {alpha}) composites on white and uses Rec.709 luminance");
        }
    }

    private static void TestSmallImagesAndCancellation()
    {
        foreach ((int width, int height) in new[] { (1, 1), (1, 7), (7, 1), (3, 5) })
        {
            byte[] input = ToBgra(Enumerable.Range(0, width * height)
                .Select(index => (byte)((index * 37 + 33) % 256)).ToArray());
            byte[] first = FourLevelDither.ProcessPixels(input, width, height, CancellationToken.None);
            byte[] second = FourLevelDither.ProcessPixels(input, width, height, CancellationToken.None);
            Require(first.Length == width * height, $"{width} x {height} preserves dimensions");
            RequirePalette(first, $"{width} x {height}");
            Require(first.SequenceEqual(second), $"{width} x {height} produces deterministic output");
        }

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try
        {
            FourLevelDither.ProcessPixels([0, 0, 0, 255], 1, 1, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Require(cancelled, "a cancelled request throws rather than returning an output image");
    }

    private static void TestBitmapAndPngRoundTrip()
    {
        const int width = 19;
        const int height = 11;
        byte[] input = ToBgra(Enumerable.Range(0, width * height)
            .Select(index => (byte)((index * 43) % 256)).ToArray());
        Bgra32Image source = new(width, height, input);
        Gray8Image processed = FourLevelDither.Process(source, CancellationToken.None);
        Require(processed.Width == width && processed.Height == height,
            "bitmap processing preserves source dimensions");
        byte[] expected = FourLevelDither.ProcessPixels(input, width, height, CancellationToken.None);
        Require(ReadGray(processed).SequenceEqual(expected), "bitmap and raw pixel processing agree");

        // Gray8 is also the format of the saved output. Processing it again must
        // preserve exact levels without introducing conversion or dither changes.
        Require(ReadGray(FourLevelDither.Process(processed.ToBgra32(), CancellationToken.None)).SequenceEqual(expected),
            "processing a four-level Gray8 bitmap is idempotent");

        string directory = Path.Combine(Path.GetTempPath(), $"four-level-dither-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "four-level.png");
            FourLevelDither.SavePng(processed, path);
            Bgra32Image reloaded = FourLevelDither.LoadImage(path);
            Require(reloaded.Width == width && reloaded.Height == height,
                "PNG round trip preserves dimensions");
            byte[] result = ReadGray(reloaded);
            RequirePalette(result, "saved PNG");
            Require(result.SequenceEqual(expected), "PNG round trip preserves every output level exactly");
            using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Require(exclusive.Length > 0, "loading the PNG does not keep its source file locked");
        }
        finally
        {
            foreach (string path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static byte[] ToBgra(byte[] gray)
    {
        byte[] pixels = new byte[gray.Length * 4];
        for (int index = 0; index < gray.Length; index++)
        {
            pixels[index * 4] = gray[index];
            pixels[index * 4 + 1] = gray[index];
            pixels[index * 4 + 2] = gray[index];
            pixels[index * 4 + 3] = 255;
        }

        return pixels;
    }

    private static byte[] ReadGray(Bgra32Image source) => TestImages.ReadGray(source);

    private static byte[] ReadGray(Gray8Image source) => TestImages.ReadGray(source);

    private static void RequirePalette(byte[] pixels, string name)
    {
        Require(pixels.All(value => value is 0 or 85 or 170 or 255),
            $"{name} contains only 0, 85, 170 and 255");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Four-level dither: {message}.");
    }
}
