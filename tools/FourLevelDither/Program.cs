using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DiscImageStudio.Imaging;
using DitherProcessor = DiscImageStudio.Imaging.GrayscaleDither;

namespace DiscImageStudio.DitherTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || (args.Length == 1 && !args[0].StartsWith("-", StringComparison.Ordinal)))
            {
                DitherApp.InitialImage = args.Length == 1 ? args[0] : null;
                return BuildAvaloniaApp().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
            }

            AttachToParentConsole();
            if (args is ["--help"] or ["-h"])
            {
                Console.WriteLine("FourLevelDither [IMAGE] — open the independent image tool.\n"
                    + "FourLevelDither --input IMAGE --output RESULT.png [--mode dvd|cd] [--gamma none|srgb] — convert without a window.\n"
                    + "Both modes output black/white 0/255. Default dvd: A5/92; cd: 0D/AA.\n"
                    + "Gamma defaults to none (existing output); srgb decodes RGB to linear light before dithering.\n"
                    + "Output: serpentine Floyd–Steinberg, original size, alpha over white.");
                return 0;
            }

            Dictionary<string, string> options = ParseOptions(args);
            DitherMode mode = options.TryGetValue("--mode", out string? modeName) ? DitherModes.Parse(modeName) : DitherMode.Dvd;
            bool linearizeSrgb = options.TryGetValue("--gamma", out string? gammaName) && ParseGamma(gammaName);
            string input = Path.GetFullPath(Required(options, "--input"));
            if (options.TryGetValue("--snapshot", out string? snapshot))
            {
                if (options.ContainsKey("--output")) throw new ArgumentException("Choose --snapshot or --output.");
                return Snapshot(input, Path.GetFullPath(snapshot), mode, linearizeSrgb);
            }

            string output = Path.GetFullPath(Required(options, "--output"));
            ValidateOutputPath(input, output);
            if (File.Exists(output)) throw new IOException("输出文件已存在，请选择另一个文件名。");
            Bgra32Image source = DitherProcessor.LoadImage(input);
            Gray8Image result = DitherProcessor.Process(source, DitherModes.Levels(mode), linearizeSrgb: linearizeSrgb);
            DitherProcessor.SavePng(result, output, DitherModes.Levels(mode));
            Console.WriteLine($"Saved {result.Width} × {result.Height}, {DitherModes.Label(mode)} ({DitherModes.PaletteLabel(mode)}), gamma={(linearizeSrgb ? "srgb" : "none")}: {output}");
            return 0;
        }
        catch (Exception exception)
        {
            AttachToParentConsole();
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static void ValidateOutputPath(string input, string output)
    {
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(output),
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请另存为新文件，不能覆盖原图。");
        if (!Path.GetExtension(output).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请保存为 PNG，以保留准确的灰阶。");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || args[index] is not ("--input" or "--output" or "--snapshot" or "--mode" or "--gamma")
                || !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("用法：FourLevelDither --input 原图 --output 结果.png [--mode dvd|cd] [--gamma none|srgb]");
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string name)
        => options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing {name}.");

    private static bool ParseGamma(string value) => value.ToLowerInvariant() switch
    {
        "none" => false,
        "srgb" => true,
        _ => throw new ArgumentException("--gamma 请选择 none（关闭）或 srgb（sRGB 伽马校正）。"),
    };

    /// <summary>Also used by the Avalonia previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<DitherApp>()
            .With(new Avalonia.Media.FontManagerOptions
            {
                DefaultFamilyName = "avares://FourLevelDither/Assets/Fonts#HarmonyOS Sans SC",
            });

    // Off-screen rendering (Avalonia.Headless + Skia, no display server) exercises the same
    // window and asynchronous processing used interactively.
    private static int Snapshot(string input, string output, DitherMode mode, bool linearizeSrgb)
    {
        ValidateOutputPath(input, output);
        BuildAvaloniaApp()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        DitherWindow window = new(null, mode, linearizeSrgb);
        window.Show();
        Task processing = window.LoadImageAsync(input);
        using CancellationTokenSource stop = new();
        Stopwatch settle = new();
        DispatcherTimer poll = new(TimeSpan.FromMilliseconds(25), DispatcherPriority.Background, (_, _) =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (!processing.IsCompleted) return;
            settle.Start();
            if (settle.ElapsedMilliseconds > 150) stop.Cancel();
        });
        poll.Start();
        Dispatcher.UIThread.MainLoop(stop.Token);
        poll.Stop();
        processing.GetAwaiter().GetResult();
        if (!window.HasResult) throw new InvalidOperationException("Snapshot input could not be processed.");
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("The headless renderer produced no frame.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        frame.Save(output, PngBitmapEncoderOptions.Default);
        window.Close();
        Console.WriteLine($"Snapshot: {output}");
        return 0;
    }

    private static void AttachToParentConsole()
    {
        // A WinExe has no console of its own on Windows; elsewhere stdio is already attached.
        if (!OperatingSystem.IsWindows() || !AttachConsole(0xFFFFFFFF)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
