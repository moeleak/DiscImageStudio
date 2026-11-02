using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiscImageStudio.Controls;

/// <summary>A collapsible parameter group: expanding animates its height from zero to the
/// content's natural height (and fades it in); collapsing reverses that.</summary>
public class RevealPanel : ContentControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<RevealPanel, bool>(nameof(IsExpanded));

    private bool? _lastExpanded;

    public RevealPanel()
    {
        ClipToBounds = true;
        Height = 0;
        Opacity = 0;
        Focusable = false;
        IsTabStop = false;
        AttachedToVisualTree += (_, _) => UpdateExpansion();
        SizeChanged += (_, args) =>
        {
            if (args.WidthChanged) UpdateExpansion();
        };
    }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty || change.Property == ContentProperty)
        {
            UpdateExpansion();
        }
    }

    private void UpdateExpansion()
    {
        bool expanded = IsExpanded;
        bool animate = IsAttachedToVisualTree() && _lastExpanded is { } previous && previous != expanded;
        _lastExpanded = expanded;
        Transitions = animate
            ?
            [
                new DoubleTransition { Property = HeightProperty, Duration = expanded ? Motion.Slow : Motion.Base, Easing = Motion.EaseOut },
                new DoubleTransition { Property = OpacityProperty, Duration = Motion.Base },
            ]
            : null;
        Height = expanded ? MeasureContentHeight() : 0;
        Opacity = expanded ? 1 : 0;
    }

    private bool IsAttachedToVisualTree() => this.GetVisualRootSafe() is not null;

    private double MeasureContentHeight()
    {
        if (Content is not Layoutable content)
        {
            return 0;
        }

        content.Measure(new Size(Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(content.DesiredSize.Height, 1);
    }
}

internal static class VisualRootExtensions
{
    internal static object? GetVisualRootSafe(this Visual visual) => TopLevel.GetTopLevel(visual);
}
