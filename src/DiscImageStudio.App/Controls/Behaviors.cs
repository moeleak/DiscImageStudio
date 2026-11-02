using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiscImageStudio.Controls;

/// <summary>Fades the top/bottom edge of a scrolling page while there is more content beyond
/// that edge, so a clipped panel reads as "continues" instead of "ends here".</summary>
public static class ScrollMaskBehavior
{
    private const double FadeLength = 0.035;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled", typeof(ScrollMaskBehavior));

    static ScrollMaskBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) =>
        {
            viewer.ScrollChanged -= OnScrollChanged;
            viewer.SizeChanged -= OnSizeChanged;
            if (args.NewValue is true)
            {
                viewer.ScrollChanged += OnScrollChanged;
                viewer.SizeChanged += OnSizeChanged;
                Update(viewer);
            }
            else
            {
                viewer.OpacityMask = null;
            }
        });
    }

    public static bool GetEnabled(ScrollViewer target) => target.GetValue(EnabledProperty);

    public static void SetEnabled(ScrollViewer target, bool value) => target.SetValue(EnabledProperty, value);

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer) Update(viewer);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer) Update(viewer);
    }

    private static void Update(ScrollViewer viewer)
    {
        bool up = viewer.Offset.Y > 0.5;
        bool down = viewer.Offset.Y + viewer.Viewport.Height < viewer.Extent.Height - 0.5;
        viewer.OpacityMask = !up && !down ? null : new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(up ? Colors.Transparent : Colors.White, 0),
                new GradientStop(Colors.White, FadeLength),
                new GradientStop(Colors.White, 1 - FadeLength),
                new GradientStop(down ? Colors.Transparent : Colors.White, 1),
            },
        };
    }
}

/// <summary>When an expander inside a scrolling page opens, scrolls the opened body into
/// view so the newly revealed controls are not left below the fold.</summary>
public static class ExpanderAutoScroll
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Expander, bool>("Enabled", typeof(ExpanderAutoScroll));

    static ExpanderAutoScroll()
    {
        Expander.IsExpandedProperty.Changed.AddClassHandler<Expander>((expander, args) =>
        {
            if (args.NewValue is true && GetEnabled(expander))
            {
                Dispatcher.UIThread.Post(() => expander.BringIntoView(), DispatcherPriority.Background);
            }
        });
    }

    public static bool GetEnabled(Expander target) => target.GetValue(EnabledProperty);

    public static void SetEnabled(Expander target, bool value) => target.SetValue(EnabledProperty, value);
}
