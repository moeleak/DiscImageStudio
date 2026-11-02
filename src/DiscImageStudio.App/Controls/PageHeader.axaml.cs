using Avalonia;
using Avalonia.Controls;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Controls;

public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> BadgeProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Badge), string.Empty);
    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Hint), string.Empty);
    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Subtitle), string.Empty);
    public static readonly StyledProperty<Workspace> WorkspaceProperty =
        AvaloniaProperty.Register<PageHeader, Workspace>(nameof(Workspace), Workspace.Home);
    public static readonly StyledProperty<Workspace> CurrentProperty =
        AvaloniaProperty.Register<PageHeader, Workspace>(nameof(Current), Workspace.Home);
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    public PageHeader()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public Workspace Workspace { get => GetValue(WorkspaceProperty); set => SetValue(WorkspaceProperty, value); }
    public Workspace Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    public event Action<Workspace>? StageActivated
    {
        add => Stages.StageActivated += value;
        remove => Stages.StageActivated -= value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActionsProperty) ActionHost.Content = Actions;
        else if (change.Property == CurrentProperty) Stages.Current = Current;
        else if (change.Property.OwnerType == typeof(PageHeader)) UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        TitleText.Text = Title;
        BadgeText.Text = Badge;
        BadgeChip.IsVisible = !string.IsNullOrWhiteSpace(Badge);
        HintSpot.Hint = Hint;
        HintSpot.IsVisible = !string.IsNullOrWhiteSpace(Hint);
        SubtitleText.Text = Subtitle;
        SubtitleText.IsVisible = !string.IsNullOrWhiteSpace(Subtitle);
        Stages.IsVisible = Workspace != Workspace.Home;
    }
}
