using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

/// <summary>Shows its content exactly when <see cref="IsOpen"/> differs from <see cref="Inverted"/>.
/// One gate replaces per-view boolean visibility plumbing for disc-family/mode branches.</summary>
public class VisibilityGate : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<VisibilityGate, bool>(nameof(IsOpen));

    public static readonly StyledProperty<bool> InvertedProperty =
        AvaloniaProperty.Register<VisibilityGate, bool>(nameof(Inverted));

    public VisibilityGate() => UpdateVisibility();

    protected override Type StyleKeyOverride => typeof(ContentControl);

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool Inverted
    {
        get => GetValue(InvertedProperty);
        set => SetValue(InvertedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty || change.Property == InvertedProperty)
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility() => IsVisible = IsOpen != Inverted;
}
