using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace DiscImageStudio.Controls;

/// <summary>Recent-project tile: a sleeve with title/family/location and the project's disc
/// face tucked behind it. Hover slides the sleeve left and the disc right, spins the face at
/// 24°/s and scrolls an overflowing location (template in Themes/CustomControls.axaml).</summary>
public class RecentProjectCard : Button
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<RecentProjectCard, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> FamilyLabelProperty =
        AvaloniaProperty.Register<RecentProjectCard, string>(nameof(FamilyLabel), string.Empty);
    public static readonly StyledProperty<string> LocationTextProperty =
        AvaloniaProperty.Register<RecentProjectCard, string>(nameof(LocationText), string.Empty);
    public static readonly StyledProperty<IImage?> ThumbnailSourceProperty =
        AvaloniaProperty.Register<RecentProjectCard, IImage?>(nameof(ThumbnailSource));

    private const double SpinDegreesPerSecond = 24;
    private const double MaxSpinStepSeconds = 0.5;

    private Control? _discSpin;
    private Control? _locationViewport;
    private TextBlock? _locationText;
    private CancellationTokenSource? _marquee;
    private bool _spinning;
    private TimeSpan? _lastSpinTick;
    private double _angle;

    public RecentProjectCard()
    {
        DetachedFromVisualTree += (_, _) =>
        {
            _spinning = false;
            Marquee.Stop(ref _marquee);
        };
    }

    protected override Type StyleKeyOverride => typeof(RecentProjectCard);

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string FamilyLabel { get => GetValue(FamilyLabelProperty); set => SetValue(FamilyLabelProperty, value); }
    public string LocationText { get => GetValue(LocationTextProperty); set => SetValue(LocationTextProperty, value); }
    public IImage? ThumbnailSource { get => GetValue(ThumbnailSourceProperty); set => SetValue(ThumbnailSourceProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _discSpin = e.NameScope.Find<Control>("PART_DiscSpin");
        _locationViewport = e.NameScope.Find<Control>("PART_LocationViewport");
        _locationText = e.NameScope.Find<TextBlock>("PART_LocationText");
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!IsEffectivelyEnabled) return;
        _spinning = true;
        _lastSpinTick = null;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
        if (_locationText is not null && _locationViewport is not null)
        {
            double overflow = _locationText.DesiredSize.Width - _locationViewport.Bounds.Width;
            if (double.IsFinite(overflow) && overflow > Marquee.OverflowTolerance)
                _marquee = Marquee.Begin(_locationText, overflow);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _spinning = false;
        Marquee.Stop(ref _marquee);
        if (_locationText is not null) _locationText.RenderTransform = null;
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_spinning || _discSpin is null) return;
        if (_lastSpinTick is { } previous)
        {
            double seconds = (time - previous).TotalSeconds;
            if (seconds > 0 && seconds <= MaxSpinStepSeconds)
            {
                _angle = (_angle + SpinDegreesPerSecond * seconds) % 360;
                _discSpin.RenderTransform = new RotateTransform(_angle);
            }
        }

        _lastSpinTick = time;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }
}
