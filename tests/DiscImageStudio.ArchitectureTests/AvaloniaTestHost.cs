using Avalonia;
using Avalonia.Headless;

/// <summary>Runs UI tests on one Avalonia.Headless UI thread with the real App (themes, fonts,
/// Skia rendering), on every platform and without a display server.</summary>
internal static class AvaloniaTestHost
{
    private static HeadlessUnitTestSession? s_session;

    /// <summary>Entry point HeadlessUnitTestSession looks up by name.</summary>
    public static AppBuilder BuildAvaloniaApp() => DiscImageStudio.Program.BuildHeadlessApp();

    internal static void Run(Action test)
    {
        s_session ??= HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestHost));
        s_session.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();
    }
}
