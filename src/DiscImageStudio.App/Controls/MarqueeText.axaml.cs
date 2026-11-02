using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiscImageStudio.Controls;

public partial class MarqueeText : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarqueeText, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<MarqueeText, bool>(nameof(IsActive));

    private static readonly IBrush EdgeFadeMask = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.White, 0),
            new GradientStop(Colors.White, 0.89),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
        },
    };

    private CancellationTokenSource? _marquee;

    public MarqueeText()
    {
        InitializeComponent();
        Viewport.SizeChanged += (_, _) => RefreshScroll();
        LabelText.SizeChanged += (_, _) => RefreshScroll();
        DetachedFromVisualTree += (_, _) => Marquee.Stop(ref _marquee);
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            LabelText.Text = Text;
            RefreshScroll();
        }
        else if (change.Property == IsActiveProperty)
        {
            RefreshScroll();
        }
    }

    private void RefreshScroll()
    {
        double textWidth = LabelText.DesiredSize.Width;
        double viewportWidth = Viewport.Bounds.Width;
        if (!double.IsFinite(textWidth) || textWidth <= 0 || viewportWidth <= 0) return;
        double overflow = textWidth - viewportWidth;
        bool overflowing = overflow > Marquee.OverflowTolerance;
        ToolTip.SetTip(this, overflowing ? Text : null);
        Viewport.OpacityMask = overflowing ? EdgeFadeMask : null;
        Marquee.Stop(ref _marquee);
        if (IsActive && overflowing)
        {
            LabelText.RenderTransform = null;
            _marquee = Marquee.Begin(LabelText, overflow);
            return;
        }

        LabelText.RenderTransform = new TranslateTransform(overflowing ? 0 : (viewportWidth - textWidth) / 2, 0);
    }
}
