using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiscImageStudio.Hosting;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio;

/// <summary>Asks for a user preset's display name before the parameter set lands in the
/// catalog. The caller has already validated the geometry.</summary>
public partial class PresetSaveWindow : Window
{
    public PresetSaveWindow()
    {
        InitializeComponent();
        WindowChromePolicy.Apply(this);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close(false);
        };
        NameBox.TextChanged += (_, _) => SaveButton.IsEnabled = PresetName.Length > 0;
        Opened += (_, _) => NameBox.Focus();
        SaveButton.IsEnabled = false;
    }

    public string PresetName => (NameBox.Text ?? string.Empty).Trim();

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>Shows the dialog over its owner; null when the user cancelled.</summary>
    public static async Task<PresetSaveResult?> ShowAsync(Window owner)
    {
        PresetSaveWindow window = new();
        return await window.ShowDialog<bool?>(owner) == true ? new PresetSaveResult(window.PresetName) : null;
    }
}
