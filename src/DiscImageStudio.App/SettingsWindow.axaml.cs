using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiscImageStudio.Core;
using DiscImageStudio.Hosting;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio;

/// <summary>Modal settings surface: categories on the left, the active category on the right,
/// Save/Cancel pinned to the bottom. Nothing is applied until Save; Cancel or Escape drops every
/// edit. The window owns the folder picker and the clear-cache confirmation.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel? _viewModel;

    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        WindowChromePolicy.Apply(this);
        DataContext = viewModel;
        AvaloniaFileDialogService dialogs = new(() => this);
        viewModel.PickDirectory = (title, start) => dialogs.PickFolderAsync(title,
            string.IsNullOrWhiteSpace(start) ? PlatformPaths.Documents : start);
        viewModel.ConfirmClearCache = ConfirmClearCacheAsync;
        AddHandler(KeyDownEvent, SettingsWindow_PreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private Task<bool> ConfirmClearCacheAsync() => ClearCacheConfirm.ShowAsync(
        "清空缓存",
        "将删除本应用写入缓存目录的缩略图、合成图与预览图片。工程文件与导出文件不受影响。",
        "清空缓存");

    internal void ShowCategoryForSnapshot(int index) => SelectCategory(index);

    private void GeneralNav_Activated(object? sender, RoutedEventArgs e) => SelectCategory(0);

    private void StorageNav_Activated(object? sender, RoutedEventArgs e) => SelectCategory(1);

    private void SelectCategory(int index)
    {
        GeneralNav.IsActive = index == 0;
        StorageNav.IsActive = index == 1;
        GeneralPanel.IsVisible = index == 0;
        StoragePanel.IsVisible = index == 1;
    }

    private void SettingsWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (ClearCacheConfirm.HandleKeyDown(e.Key)) return;
        Close(false);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.TrySave() == true) Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
