using Avalonia.Controls;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Views;

public partial class LivePreviewView : UserControl
{
    public LivePreviewView() => InitializeComponent();

    public LivePreviewView(LivePreviewViewModel viewModel)
        : this() => DataContext = viewModel;
}
