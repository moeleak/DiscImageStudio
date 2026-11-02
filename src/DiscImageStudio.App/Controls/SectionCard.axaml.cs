using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace DiscImageStudio.Controls;

/// <summary>A plain form card: header row (optional icon + title) above arbitrary content.</summary>
public partial class SectionCard : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<SectionCard, string>(nameof(Header), string.Empty);
    public static readonly StyledProperty<string?> IconKindProperty =
        AvaloniaProperty.Register<SectionCard, string?>(nameof(IconKind));
    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<SectionCard, object?>(nameof(Body));

    public SectionCard()
    {
        InitializeComponent();
        UpdateHeader();
    }

    public string Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }

    [Content]
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeaderProperty || change.Property == IconKindProperty) UpdateHeader();
        else if (change.Property == BodyProperty) BodyPresenter.Content = Body;
    }

    private void UpdateHeader()
    {
        HeaderText.Text = Header;
        bool hasIcon = !string.IsNullOrEmpty(IconKind);
        HeaderIcon.Kind = IconKind;
        HeaderIcon.IsVisible = hasIcon;
        HeaderText.Margin = hasIcon ? new Thickness(8, 0, 0, 0) : default;
    }
}
