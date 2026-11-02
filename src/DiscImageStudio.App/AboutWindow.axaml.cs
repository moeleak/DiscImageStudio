using Avalonia.Controls;
using DiscImageStudio.Hosting;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio;

/// <summary>About dialog: a fixed-size read-only card sharing the app's window chrome.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    public AboutWindow(AboutViewModel viewModel)
        : this()
    {
        WindowChromePolicy.Apply(this);
        DataContext = viewModel;
    }
}
