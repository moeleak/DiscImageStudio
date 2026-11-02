using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

public partial class EmptyStateCard : UserControl
{
    public static readonly StyledProperty<string> IconKindProperty =
        AvaloniaProperty.Register<EmptyStateCard, string>(nameof(IconKind), "Disc");
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateCard, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<EmptyStateCard, string>(nameof(Description), string.Empty);
    public static readonly StyledProperty<string> ActionLabelProperty =
        AvaloniaProperty.Register<EmptyStateCard, string>(nameof(ActionLabel), string.Empty);
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyStateCard, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<object?> ActionCommandParameterProperty =
        AvaloniaProperty.Register<EmptyStateCard, object?>(nameof(ActionCommandParameter));

    public EmptyStateCard()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    public string IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string ActionLabel { get => GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public object? ActionCommandParameter { get => GetValue(ActionCommandParameterProperty); set => SetValue(ActionCommandParameterProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.OwnerType == typeof(EmptyStateCard)) UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        IconHost.Kind = IconKind;
        TitleText.Text = Title;
        TitleText.IsVisible = !string.IsNullOrWhiteSpace(Title);
        DescriptionText.Text = Description;
        DescriptionText.IsVisible = !string.IsNullOrWhiteSpace(Description);
        ActionButton.Content = ActionLabel;
        ActionButton.Command = ActionCommand;
        ActionButton.CommandParameter = ActionCommandParameter;
        ActionButton.IsVisible = ActionCommand is not null && !string.IsNullOrWhiteSpace(ActionLabel);
    }
}
