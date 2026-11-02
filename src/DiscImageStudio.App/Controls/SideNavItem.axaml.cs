using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace DiscImageStudio.Controls;

public partial class SideNavItem : UserControl
{
    public static readonly StyledProperty<string?> IconKindProperty =
        AvaloniaProperty.Register<SideNavItem, string?>(nameof(IconKind));
    public static readonly StyledProperty<string> LabelTextProperty =
        AvaloniaProperty.Register<SideNavItem, string>(nameof(LabelText), string.Empty);
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SideNavItem, bool>(nameof(IsActive));
    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<SideNavItem, bool>(nameof(IsCollapsed));
    public static readonly StyledProperty<double> IndentProperty =
        AvaloniaProperty.Register<SideNavItem, double>(nameof(Indent));
    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<SideNavItem, double>(nameof(LabelFontSize), 14.0);

    public static readonly RoutedEvent<RoutedEventArgs> ActivatedEvent =
        RoutedEvent.Register<SideNavItem, RoutedEventArgs>(nameof(Activated), RoutingStrategies.Bubble);

    public SideNavItem()
    {
        InitializeComponent();
        UpdateVisual();
    }

    public string? IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public string LabelText { get => GetValue(LabelTextProperty); set => SetValue(LabelTextProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public bool IsCollapsed { get => GetValue(IsCollapsedProperty); set => SetValue(IsCollapsedProperty, value); }
    public double Indent { get => GetValue(IndentProperty); set => SetValue(IndentProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    public event EventHandler<RoutedEventArgs>? Activated
    {
        add => AddHandler(ActivatedEvent, value);
        remove => RemoveHandler(ActivatedEvent, value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            RaiseEvent(new RoutedEventArgs(ActivatedEvent, this));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.OwnerType == typeof(SideNavItem)) UpdateVisual();
    }

    private void UpdateVisual()
    {
        Classes.Set("active", IsActive);
        LabelTextBlock.Text = LabelText;
        LabelTextBlock.FontSize = LabelFontSize;
        bool hasIcon = !string.IsNullOrEmpty(IconKind);
        Icon.Kind = IconKind;
        Icon.IsVisible = hasIcon;
        // Icon entries drop their label in the collapsed rail and show it as a tooltip instead;
        // icon-less entries keep their short label.
        LabelTextBlock.IsVisible = !hasIcon || !IsCollapsed;
        ToolTip.SetTip(Root, IsCollapsed && hasIcon ? LabelText : null);
        if (IsCollapsed)
        {
            ItemContent.Margin = default;
            ItemContent.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            ItemContent.Margin = new Thickness(12 + Indent, 0, 12, 0);
            ItemContent.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }
}
