using DiscImageStudio.Localization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiscImageStudio.Hosting;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio;

/// <summary>New-project creation and project-settings editing share one dialog. The view
/// model validates; this class wires pickers, the close path and Escape.</summary>
public partial class ProjectDialogWindow : Window
{
    public ProjectDialogWindow() => InitializeComponent();

    public ProjectDialogWindow(ProjectDialogViewModel viewModel)
        : this()
    {
        WindowChromePolicy.Apply(this);
        ViewModel = viewModel;
        AvaloniaFileDialogService dialogs = new(() => this);
        viewModel.ShowPresetSaveDialog = () => PresetSaveWindow.ShowAsync(this);
        viewModel.PickSaveDirectory = initial => dialogs.PickFolderAsync(L.T("选择工程保存位置"), initial);
        viewModel.CloseWithConfirm = () => Close(true);
        viewModel.ConfirmPresetSwitch = tile => PresetSwitchConfirm.ShowAsync(
            L.T("切换预设"),
            L.F($"切换为“{tile.Title}”会用该预设的几何参数覆盖当前参数，确认后无法撤销。是否继续？"),
            L.T("覆盖参数"));
        DataContext = viewModel;
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            // Escape answers the overwrite guard first; only an unguarded Escape closes.
            if (!PresetSwitchConfirm.HandleKeyDown(args.Key)) Close(false);
        }, RoutingStrategies.Tunnel);
    }

    public ProjectDialogViewModel? ViewModel { get; }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Card_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: ProjectTemplateTile { IsUserPreset: true } tile } card || ViewModel is null)
        {
            e.Handled = true;
            return;
        }

        MenuItem delete = new() { Header = L.T("Xaml.DeletePreset") };
        delete.Click += (_, _) =>
        {
            if (ViewModel.DeletePresetCommand.CanExecute(tile)) ViewModel.DeletePresetCommand.Execute(tile);
        };
        new ContextMenu { ItemsSource = new[] { delete } }.Open(card);
        e.Handled = true;
    }
}
