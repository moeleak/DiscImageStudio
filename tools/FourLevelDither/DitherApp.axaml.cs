using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DiscImageStudio.DitherTool;

public partial class DitherApp : Application
{
    /// <summary>Image passed on the command line, opened once the window shows.</summary>
    internal static string? InitialImage { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new DitherWindow(InitialImage);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
