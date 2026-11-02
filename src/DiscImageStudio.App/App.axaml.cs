using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using DiscImageStudio.Hosting;
using DiscImageStudio.Services;

namespace DiscImageStudio;

public partial class App : Application
{
    public override void Initialize()
    {
        TransformKeyFrameAnimator.Register();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettingsStore store = new();
            AppSettings settings = store.Load();
            ApplyTheme(settings.Theme);
            // New jobs honor the saved cache folder; earlier files keep their paths.
            TempFiles.TryConfigureCacheDirectory(settings.CacheDirectory);
            MainWindow window = new(settings, store);
            desktop.MainWindow = window;
            desktop.ShutdownRequested += async (_, args) =>
            {
                // Logoff/shutdown (or ⌘Q): unsaved work gets the same save/discard/cancel prompt.
                if (!window.HasUnsavedWork) return;
                args.Cancel = true;
                if (await window.ConfirmSessionEndAsync()) desktop.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Switches every window at once: the tokens are theme dictionaries, so
    /// DynamicResource consumers and Fluent controls restyle live. "System" follows the OS
    /// (Windows, macOS and the freedesktop color-scheme setting on Linux).</summary>
    internal static void ApplyTheme(AppTheme theme)
    {
        if (Current is not { } application) return;
        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
