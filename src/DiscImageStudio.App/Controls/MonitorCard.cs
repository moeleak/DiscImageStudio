using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

/// <summary>Monitor card: banded header (icon + title + subtitle + action slot), body and an
/// optional status footer. Templated (not a UserControl) so page content keeps the page's
/// name scope; the template lives in Themes/CustomControls.axaml.</summary>
public class MonitorCard : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MonitorCard, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<MonitorCard, string>(nameof(Subtitle), string.Empty);
    public static readonly StyledProperty<string?> IconKindProperty =
        AvaloniaProperty.Register<MonitorCard, string?>(nameof(IconKind));
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<MonitorCard, object?>(nameof(Actions));
    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<MonitorCard, string>(nameof(StatusText), string.Empty);
    public static readonly StyledProperty<object?> StatusContentProperty =
        AvaloniaProperty.Register<MonitorCard, object?>(nameof(StatusContent));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public string StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public object? StatusContent { get => GetValue(StatusContentProperty); set => SetValue(StatusContentProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusTextProperty || change.Property == StatusContentProperty)
        {
            PseudoClasses.Set(":has-status-content", StatusContent is not null);
            PseudoClasses.Set(":has-status", StatusContent is not null || !string.IsNullOrWhiteSpace(StatusText));
        }
    }
}
