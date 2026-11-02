using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Controls;

public sealed class StageItem
{
    internal StageItem(Workspace workspace, string label, int index, Workspace current)
    {
        Workspace = workspace;
        Label = label;
        IndexText = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IsBranch = workspace == Workspace.Calibration;
        IsCurrent = workspace == current;
    }

    public Workspace Workspace { get; }
    public string Label { get; }
    public string IndexText { get; }
    public bool IsBranch { get; }
    public bool IsCurrent { get; }
}

public partial class StageBar : UserControl
{
    public static readonly StyledProperty<Workspace> CurrentProperty =
        AvaloniaProperty.Register<StageBar, Workspace>(nameof(Current), Workspace.Home);

    private static readonly (Workspace Workspace, string Label)[] Stages =
    [
        (Workspace.Design, "设计"),
        (Workspace.Preview, "预览"),
        (Workspace.Export, "导出"),
        (Workspace.Burn, "刻录"),
        (Workspace.Calibration, "标定"),
    ];

    public event Action<Workspace>? StageActivated;

    public StageBar()
    {
        InitializeComponent();
        RebuildItems();
    }

    public Workspace Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentProperty) RebuildItems();
    }

    private void RebuildItems()
        => StageList.ItemsSource = Stages.Select((stage, index) => new StageItem(stage.Workspace, stage.Label, index + 1, Current)).ToList();

    private void Stage_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is StageItem item) StageActivated?.Invoke(item.Workspace);
    }
}
