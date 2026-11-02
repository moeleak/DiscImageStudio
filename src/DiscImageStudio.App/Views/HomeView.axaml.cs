using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    public HomeView(HomeViewModel viewModel)
        : this() => DataContext = viewModel;

    private void RemoveRecent_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RecentProjectTile tile && DataContext is HomeViewModel home
            && home.RemoveRecentCommand.CanExecute(tile))
        {
            home.RemoveRecentCommand.Execute(tile);
        }
    }
}
