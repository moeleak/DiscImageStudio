using Avalonia.Controls;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Views;

public partial class ExportView : UserControl
{
    public ExportView() => InitializeComponent();

    public ExportView(ExportViewModel viewModel)
        : this() => DataContext = viewModel;
}
