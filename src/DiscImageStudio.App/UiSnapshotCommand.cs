using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DiscImageStudio.Core;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio;

/// <summary>The host-only <c>ui-snapshot</c> command: renders a workspace or dialog off screen
/// to PNG through Avalonia.Headless and Skia, so it needs no display server on any platform.</summary>
internal static class UiSnapshotCommand
{
    // Long enough for the 250 ms transitions (nav highlight, toast, sidebar) to settle.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(450);

    internal static int Run(string[] arguments)
    {
        ReadOnlySpan<string> options = arguments.AsSpan(1);
        string? tabValue = UnifiedCommandRunner.FindOption(options, "tab");
        int workspaceIndex = tabValue is null ? 0 : int.Parse(tabValue);
        if (workspaceIndex < 0 || workspaceIndex > (int)Workspace.Calibration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                "Tabs: 0 home, 1 design, 2 preview, 3 export, 4 burn, 5 calibration.");
        }

        bool settingsDialog = UnifiedCommandRunner.HasOption(options, "settings-dialog");
        SnapshotRequest request = new(
            Path.GetFullPath(UnifiedCommandRunner.RequireOption(options, "output")),
            (Workspace)workspaceIndex,
            UnifiedCommandRunner.FindOption(options, "preview"),
            UnifiedCommandRunner.FindOption(options, "live-input"),
            UnifiedCommandRunner.FindOption(options, "live-disc"),
            UnifiedCommandRunner.FindOption(options, "live-mode"),
            int.Parse(UnifiedCommandRunner.FindOption(options, "width") ?? (settingsDialog ? "760" : "1366")),
            int.Parse(UnifiedCommandRunner.FindOption(options, "height") ?? (settingsDialog ? "600" : "768")),
            UnifiedCommandRunner.FindOption(options, "calibration-session"),
            UnifiedCommandRunner.FindOption(options, "calibration-view"),
            UnifiedCommandRunner.FindOption(options, "project"),
            UnifiedCommandRunner.FindOption(options, "project-dialog"),
            UnifiedCommandRunner.HasOption(options, "about-dialog"),
            UnifiedCommandRunner.HasOption(options, "preset-dialog"),
            UnifiedCommandRunner.HasOption(options, "dock"),
            UnifiedCommandRunner.FindOption(options, "burn-source"),
            UnifiedCommandRunner.HasOption(options, "expand"),
            int.Parse(UnifiedCommandRunner.FindOption(options, "scroll") ?? "0"),
            UnifiedCommandRunner.HasOption(options, "home-hover"),
            UnifiedCommandRunner.HasOption(options, "project-update"),
            UnifiedCommandRunner.HasOption(options, "project-save"),
            UnifiedCommandRunner.HasOption(options, "tone-preview"),
            UnifiedCommandRunner.FindOption(options, "burn-progress"),
            UnifiedCommandRunner.FindOption(options, "theme"),
            settingsDialog,
            UnifiedCommandRunner.FindOption(options, "settings-view"),
            UnifiedCommandRunner.FindOption(options, "language"),
            double.Parse(UnifiedCommandRunner.FindOption(options, "scale") ?? "1",
                System.Globalization.CultureInfo.InvariantCulture));
        return Render(request);
    }

    private static int Render(SnapshotRequest request)
    {
        // The settings window is 760 px wide, so its snapshots relax the page minimum.
        int minimumWidth = request.SettingsDialog ? 640 : 800;
        if (request.Width < minimumWidth || request.Height < 600 || request.Width > 4096 || request.Height > 2160)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Snapshot dimensions must be at least 800×600 (640×600 for the settings dialog) and at most 4096×2160.");
        if (request.Scale is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(request), "Snapshot scale must be between 1 and 3.");
        AppTheme theme = request.Theme?.ToLowerInvariant() switch
        {
            null or "" or "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            "system" => AppTheme.System,
            _ => throw new ArgumentException("Snapshot theme must be dark, light or system."),
        };

        if (request.Language is not (null or L.Chinese or L.TraditionalChinese or L.English or L.SystemLanguage))
            throw new ArgumentException("Snapshot language must be zh-CN, zh-TW, en or system.");
        // Snapshots default to Simplified Chinese so reference images stay stable.
        L.Initialize(request.Language ?? L.Chinese);
        Program.BuildHeadlessApp().SetupWithoutStarting();
        App.ApplyTheme(theme);

        if (request.SettingsDialog) return RenderSettingsDialog(request, theme);
        if (request.ProjectDialog is not null) return RenderProjectDialog(request);
        if (request.AboutDialog) return Save(new AboutWindow(new AboutViewModel()), request);
        if (request.PresetDialog) return Save(new PresetSaveWindow(), request, sizeToContent: true);
        return RenderMainWindow(request);
    }

    private static int RenderMainWindow(SnapshotRequest request)
    {
        // The tone module lives in the design page's right rail; its preview only renders while
        // that workspace is active.
        Workspace workspace = request.TonePreview ? Workspace.Design : request.Workspace;
        MainWindow window = new() { WindowState = WindowState.Normal };
        window.ConfigureSnapshot(workspace, request.PreviewPath, request.LiveInputPath, request.LiveDisc,
            request.LiveMode, request.ProjectPath, request.ExpandJobDock, request.BurnSource);
        if (request.BurnProgress is not null) window.ShowBurnProgressForSnapshot(request.BurnProgress);
        Show(window, request);
        if (request.HomeHover) window.ShowHomeHoverForSnapshot();
        if (!string.IsNullOrWhiteSpace(request.CalibrationSessionPath))
        {
            Task loading = window.LoadCalibrationSnapshotAsync(request.CalibrationSessionPath);
            PumpUntil(() => loading.IsCompleted, TimeSpan.FromSeconds(60), "Calibration snapshot");
            loading.GetAwaiter().GetResult();
            window.UpdateLayout();
        }

        if (!string.IsNullOrWhiteSpace(request.CalibrationView))
        {
            window.ConfigureCalibrationSnapshotView(request.CalibrationView);
            window.UpdateLayout();
        }

        if (!string.IsNullOrWhiteSpace(request.LiveInputPath))
            PumpUntil(() => window.IsLivePreviewReady, TimeSpan.FromSeconds(30), "Live preview");
        if (request.TonePreview)
            PumpUntil(() => window.IsTonePreviewReady, TimeSpan.FromSeconds(90), "Tone preview");
        if (request.ExpandDisclosure) window.ExpandDisclosureForSnapshot();
        if (request.ProjectUpdate) window.PlayProjectUpdateForSnapshot();
        if (request.ProjectSave) window.PlayProjectSaveForSnapshot();
        window.UpdateLayout();
        window.ScrollWorkspace(request.ScrollOffset);
        return Save(window, request, alreadyShown: true);
    }

    /// <summary>Renders the project dialog standalone (create or edit mode).</summary>
    private static int RenderProjectDialog(SnapshotRequest request)
    {
        string mode = request.ProjectDialog!;
        if (mode is not ("new" or "edit" or "bd"))
            throw new ArgumentException("Project dialog snapshot mode must be new, edit or bd.");
        DiscParametersState state = new();
        state.TryLoadPresets();
        OpticalDiscFamily family = mode switch
        {
            "edit" => OpticalDiscFamily.Dvd,
            "bd" => OpticalDiscFamily.BluRay,
            _ => OpticalDiscFamily.CompactDisc,
        };
        ProjectDialogViewModel viewModel = new(state,
            new ProjectDialogRequest(EditMode: mode == "edit", PreselectFamily: family));
        if (mode == "edit")
        {
            viewModel.Family = OpticalDiscFamily.Dvd;
            viewModel.Name = "示例工程";
            viewModel.InnerRadiusText = "24.0";
            viewModel.OuterRadiusText = "58.0";
            viewModel.SectorsText = "2295104";
            viewModel.PitchLinearText = "0.001";
        }

        return Save(new ProjectDialogWindow(viewModel), request);
    }

    /// <summary>Renders the settings window standalone. The store points at a throwaway file so
    /// a snapshot never reads or rewrites the workstation's real settings.</summary>
    private static int RenderSettingsDialog(SnapshotRequest request, AppTheme theme)
    {
        int category = request.SettingsView?.ToLowerInvariant() switch
        {
            null or "" or "general" => 0,
            "storage" => 1,
            _ => throw new ArgumentException("Settings snapshot view must be general or storage."),
        };
        AppSettingsStore store = new(Path.Combine(Path.GetTempPath(),
            "disc-settings-snapshot-" + Guid.NewGuid().ToString("N") + ".json"));
        AppSettings settings = new() { Theme = theme, Language = request.Language ?? L.Chinese };
        SettingsWindow window = new(new SettingsViewModel(settings, store));
        window.ShowCategoryForSnapshot(category);
        return Save(window, request);
    }

    private static void Show(Window window, SnapshotRequest request, bool sizeToContent = false)
    {
        if (!sizeToContent)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Width = request.Width;
            window.Height = request.Height;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(0, 0);
        window.ShowInTaskbar = false;
        window.Show();
        if (request.Scale != 1) window.SetRenderScaling(request.Scale);
        window.UpdateLayout();
        Settle(window);
    }

    private static int Save(Window window, SnapshotRequest request, bool sizeToContent = false, bool alreadyShown = false)
    {
        if (!alreadyShown) Show(window, request, sizeToContent);
        Settle(window);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("The headless renderer produced no frame.");
        string? directory = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        frame.Save(request.OutputPath, PngBitmapEncoderOptions.Default);
        window.Close();
        Console.WriteLine($"UI snapshot complete: output={request.OutputPath} size={frame.PixelSize.Width}x{frame.PixelSize.Height}");
        return 0;
    }

    /// <summary>Lets queued work, debounced timers and transitions run so the captured frame
    /// shows the settled state instead of frame zero of an animation.</summary>
    private static void Settle(TopLevel topLevel)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        PumpUntil(() => stopwatch.Elapsed >= SettleTime, SettleTime + TimeSpan.FromSeconds(5), "Layout settle");
        topLevel.UpdateLayout();
    }

    /// <summary>Runs the UI dispatcher (jobs, timers and the headless render clock) until the
    /// condition holds or the timeout elapses.</summary>
    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string what)
    {
        if (condition()) return;
        Stopwatch stopwatch = Stopwatch.StartNew();
        using CancellationTokenSource stop = new();
        DispatcherTimer poll = new(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (condition() || stopwatch.Elapsed > timeout) stop.Cancel();
        });
        poll.Start();
        try
        {
            Dispatcher.UIThread.MainLoop(stop.Token);
        }
        finally
        {
            poll.Stop();
        }

        if (!condition()) throw new TimeoutException($"{what} did not finish within the snapshot timeout.");
    }

    private sealed record SnapshotRequest(
        string OutputPath,
        Workspace Workspace,
        string? PreviewPath,
        string? LiveInputPath,
        string? LiveDisc,
        string? LiveMode,
        int Width,
        int Height,
        string? CalibrationSessionPath,
        string? CalibrationView,
        string? ProjectPath,
        string? ProjectDialog,
        bool AboutDialog,
        bool PresetDialog,
        bool ExpandJobDock,
        string? BurnSource,
        bool ExpandDisclosure,
        int ScrollOffset,
        bool HomeHover,
        bool ProjectUpdate,
        bool ProjectSave,
        bool TonePreview,
        string? BurnProgress,
        string? Theme,
        bool SettingsDialog,
        string? SettingsView,
        string? Language,
        double Scale);
}
