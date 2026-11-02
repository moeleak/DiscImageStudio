using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;

namespace DiscImageStudio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            // Language is fixed before any resource dictionary, window or view model exists:
            // themes and templates resolve {loc:Text} once, at load time.
            AppSettingsStore store = new();
            AppSettings settings = store.Load();
            L.Initialize(settings.Language);
            App.StartupSettings = (settings, store);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);
        }

        AttachToParentConsole();
        return UnifiedCommandRunner.Run(arguments,
            (command, all) => command == "ui-snapshot" ? UiSnapshotCommand.Run(all) : null);
    }

    /// <summary>Also used by the Avalonia previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithAppFonts()
            .LogToTrace();

    /// <summary>Off-screen Skia rendering with no display server: ui-snapshot and the UI tests.</summary>
    internal static AppBuilder BuildHeadlessApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithAppFonts();

    /// <summary>The bundled HarmonyOS Sans SC is the default family on every platform, so
    /// Chinese UI text never depends on which CJK fonts the system happens to have.</summary>
    internal static AppBuilder WithAppFonts(this AppBuilder builder)
        => builder.With(new Avalonia.Media.FontManagerOptions
        {
            DefaultFamilyName = AppFontFamily,
        });

    internal const string AppFontFamily = "avares://DiscImageStudio/Assets/Fonts#HarmonyOS Sans SC";

    private static void AttachToParentConsole()
    {
        // A WinExe has no console of its own on Windows; elsewhere stdio is already attached.
        if (!OperatingSystem.IsWindows() || !AttachConsole(AttachParentProcess))
        {
            return;
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
