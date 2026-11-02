using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiscImageStudio.Controls;

/// <summary>Saved-project acknowledgement: one green segment runs once around the project
/// header's rounded rectangle. The head follows a cubic Bezier timing curve and the tail
/// length follows the same curve's instantaneous speed. Presentation only: never
/// hit-testable.</summary>
public sealed class ProjectSaveTrail : Control
{
    private const double LapDurationMs = 1150;
    private const double FadeInFraction = 0.07;
    private const double FadeOutFraction = 0.18;
    private const double SegmentCount = 30;
    private const double PathSampleStep = 1.5;
    // Cubic Bezier(0.55, 0, 0.2, 1): slow start, fast middle, long settle at the end.
    private const double EaseX1 = 0.55;
    private const double EaseY1 = 0;
    private const double EaseX2 = 0.2;
    private const double EaseY2 = 1;
    private static readonly double PeakSpeed = ComputeMaximumSpeed();

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<ProjectSaveTrail, IBrush>(nameof(Stroke), new SolidColorBrush(Color.FromRgb(0x17, 0xC9, 0x64)));
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<ProjectSaveTrail, double>(nameof(CornerRadius), 8.0);
    public static readonly StyledProperty<double> CoreThicknessProperty =
        AvaloniaProperty.Register<ProjectSaveTrail, double>(nameof(CoreThickness), 2.0);
    public static readonly StyledProperty<double> TrailLengthProperty =
        AvaloniaProperty.Register<ProjectSaveTrail, double>(nameof(TrailLength), 62.0);

    private readonly Stopwatch _clock = new();
    private readonly List<Point> _path = [];
    private readonly List<double> _distances = [];
    private bool _rendering;
    private bool _playPending;
    private bool _hasSnapshot;
    private double _snapshotProgress;
    private double _totalLength;
    private Size _builtSize;
    private double _builtRadius = double.NaN;
    private RadialGradientBrush? _glowBrush;
    private Pen[] _corePens = [];
    private Pen[] _haloPens = [];
    private SolidColorBrush? _headBrush;
    private Color _penCore;
    private double _penThickness = double.NaN;

    static ProjectSaveTrail()
    {
        AffectsRender<ProjectSaveTrail>(StrokeProperty, CornerRadiusProperty, CoreThicknessProperty, TrailLengthProperty);
    }

    public ProjectSaveTrail()
    {
        IsHitTestVisible = false;
        SizeChanged += (_, _) => OnSizeChanged();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double CoreThickness { get => GetValue(CoreThicknessProperty); set => SetValue(CoreThicknessProperty, value); }

    /// <summary>Length of the tail at full speed.</summary>
    public double TrailLength { get => GetValue(TrailLengthProperty); set => SetValue(TrailLengthProperty, value); }

    /// <summary>Runs the lap once. A second call while running restarts it.</summary>
    public void Play()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _playPending = true;
            return;
        }

        _playPending = false;
        _hasSnapshot = false;
        EnsurePath();
        _clock.Restart();
        if (!_rendering)
        {
            _rendering = true;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
        }

        InvalidateVisual();
    }

    public void Stop()
    {
        _rendering = false;
        _clock.Stop();
    }

    /// <summary>Snapshot hook: paints one exact frame of the lap without running the clock.</summary>
    internal void ShowAt(double progress)
    {
        _playPending = false;
        Stop();
        _hasSnapshot = true;
        _snapshotProgress = Math.Clamp(progress, 0, 1);
        InvalidateVisual();
    }

    internal void ClearSnapshot()
    {
        _hasSnapshot = false;
        InvalidateVisual();
    }

    private void OnSizeChanged()
    {
        _builtSize = default;
        if (_playPending) Play();
    }

    private void OnFrame(TimeSpan _)
    {
        if (!_rendering) return;
        double progress = _clock.Elapsed.TotalMilliseconds / LapDurationMs;
        if (progress >= 1)
        {
            Stop();
            _hasSnapshot = false;
            InvalidateVisual();
            return;
        }

        _snapshotProgress = progress;
        _hasSnapshot = true;
        InvalidateVisual();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    public override void Render(DrawingContext drawingContext)
    {
        base.Render(drawingContext);
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || !_hasSnapshot) return;
        EnsurePath();
        if (_totalLength <= 0 || _path.Count < 2) return;
        DrawTrail(drawingContext, _snapshotProgress);
    }

    private void DrawTrail(DrawingContext drawingContext, double progress)
    {
        double eased = EaseProgress(progress);
        double fade = Fade(progress);
        if (fade <= 0) return;
        double trail = TrailLengthAt(progress, TrailLength);
        double head = eased * _totalLength;
        double tail = Math.Max(0, head - trail);
        if (head - tail < 1) return;
        Color core = Stroke is ISolidColorBrush solid ? solid.Color : Color.FromRgb(0x17, 0xC9, 0x64);
        double thickness = CoreThickness;
        EnsureBrushes(core, thickness);
        using (drawingContext.PushOpacity(fade))
        {
            // A wide low-alpha halo under a crisp core reads as light without a blur effect.
            DrawTail(drawingContext, tail, head, _haloPens);
            DrawTail(drawingContext, tail, head, _corePens);
            Point headPoint = PointAt(head);
            double glowRadius = Math.Max(7, thickness * 4.5);
            drawingContext.DrawEllipse(_glowBrush, null, headPoint, glowRadius, glowRadius);
            drawingContext.DrawEllipse(_headBrush, null, headPoint, thickness * 1.15, thickness * 1.15);
        }
    }

    private void DrawTail(DrawingContext drawingContext, double tail, double head, Pen[] pens)
    {
        int segments = pens.Length;
        for (int index = 0; index < segments; index++)
        {
            double from = tail + (head - tail) * index / segments;
            double to = tail + (head - tail) * (index + 1) / segments;
            drawingContext.DrawLine(pens[index], PointAt(from), PointAt(to));
        }
    }

    private void EnsureBrushes(Color core, double thickness)
    {
        if (_corePens.Length == (int)SegmentCount && _penCore == core && Math.Abs(_penThickness - thickness) < 0.001) return;
        _penCore = core;
        _penThickness = thickness;
        int segments = (int)SegmentCount;
        _corePens = new Pen[segments];
        _haloPens = new Pen[segments];
        for (int index = 0; index < segments; index++)
        {
            double t = (index + 1) / (double)segments;
            double ramp = t * t;
            _corePens[index] = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(core.A * ramp), core.R, core.G, core.B)),
                thickness, lineCap: PenLineCap.Round);
            _haloPens[index] = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(46 * ramp), core.R, core.G, core.B)),
                thickness * 3.2, lineCap: PenLineCap.Round);
        }

        _glowBrush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(120, core.R, core.G, core.B), 0),
                new GradientStop(Color.FromArgb(42, core.R, core.G, core.B), 0.55),
                new GradientStop(Color.FromArgb(0, core.R, core.G, core.B), 1),
            },
        };
        _headBrush = new SolidColorBrush(Color.FromArgb(255, core.R, core.G, core.B));
    }

    private static double Length(Vector vector) => vector.Length;

    private static double Fade(double progress)
    {
        double rise = Math.Clamp(progress / FadeInFraction, 0, 1);
        double fall = Math.Clamp((1 - progress) / FadeOutFraction, 0, 1);
        return Math.Min(rise, fall);
    }

    private static double ComputeMaximumSpeed()
    {
        double max = 0;
        for (int step = 0; step <= 100; step++)
        {
            double p = step / 100.0;
            double speed = (CubicBezier(Math.Min(1, p + 0.01))
                - CubicBezier(Math.Max(0, p - 0.01))) / 0.02;
            max = Math.Max(max, speed);
        }

        return max;
    }

    private void EnsurePath()
    {
        double radius = Math.Clamp(CornerRadius, 0, Math.Min(Bounds.Width, Bounds.Height) / 2);
        if (_builtSize == Bounds.Size && Math.Abs(_builtRadius - radius) < 0.01 && _path.Count > 0)
        {
            return;
        }

        _builtSize = Bounds.Size;
        _builtRadius = radius;
        _path.Clear();
        _distances.Clear();

        double inset = 1.5;
        double left = inset;
        double top = inset;
        double right = Bounds.Width - inset;
        double bottom = Bounds.Height - inset;
        if (right <= left || bottom <= top)
        {
            _totalLength = 0;
            return;
        }

        double r = Math.Min(radius, Math.Min(right - left, bottom - top) / 2);
        AddLine(new Point(left + r, top), new Point(right - r, top));
        AddArc(new Point(right - r, top + r), r, -90, 0);
        AddLine(new Point(right, top + r), new Point(right, bottom - r));
        AddArc(new Point(right - r, bottom - r), r, 0, 90);
        AddLine(new Point(right - r, bottom), new Point(left + r, bottom));
        AddArc(new Point(left + r, bottom - r), r, 90, 180);
        AddLine(new Point(left, bottom - r), new Point(left, top + r));
        AddArc(new Point(left + r, top + r), r, 180, 270);

        // Close the final pixel back onto the first point so the lap is seamless.
        if (_path.Count > 0)
        {
            AddPoint(_path[0]);
        }

        _distances.Clear();
        double distance = 0;
        _distances.Add(0);
        for (int index = 1; index < _path.Count; index++)
        {
            distance += Length(PointAtRaw(index) - PointAtRaw(index - 1));
            _distances.Add(distance);
        }

        _totalLength = distance;
    }

    private void AddLine(Point from, Point to)
    {
        if (_path.Count == 0)
        {
            AddPoint(from);
        }

        double length = Length(to - from);
        int steps = Math.Max(1, (int)Math.Ceiling(length / PathSampleStep));
        for (int step = 1; step <= steps; step++)
        {
            AddPoint(new Point(
                from.X + (to.X - from.X) * step / steps,
                from.Y + (to.Y - from.Y) * step / steps));
        }
    }

    private void AddArc(Point center, double radius, double startDegrees, double endDegrees)
    {
        if (radius <= 0)
        {
            return;
        }

        double sweep = endDegrees - startDegrees;
        double length = Math.Abs(sweep) * Math.PI / 180.0 * radius;
        int steps = Math.Max(1, (int)Math.Ceiling(length / PathSampleStep));
        for (int step = 1; step <= steps; step++)
        {
            double angle = (startDegrees + sweep * step / steps) * Math.PI / 180.0;
            AddPoint(new Point(
                center.X + radius * Math.Cos(angle),
                center.Y + radius * Math.Sin(angle)));
        }
    }

    private void AddPoint(Point point)
    {
        if (_path.Count > 0 && Length(_path[^1] - point) < 0.01)
        {
            return;
        }

        _path.Add(point);
    }

    private Point PointAt(double distance)
    {
        if (_distances.Count == 0)
        {
            return default;
        }

        if (distance <= 0)
        {
            return _path[0];
        }

        if (distance >= _totalLength)
        {
            return _path[^1];
        }

        int index = _distances.BinarySearch(distance);
        if (index < 0)
        {
            index = ~index;
        }

        if (index <= 0)
        {
            return _path[0];
        }

        if (index >= _distances.Count)
        {
            return _path[^1];
        }

        double previous = _distances[index - 1];
        double span = _distances[index] - previous;
        double fraction = span <= 0 ? 0 : (distance - previous) / span;
        Point from = _path[index - 1];
        Point to = _path[index];
        return new Point(
            from.X + (to.X - from.X) * fraction,
            from.Y + (to.Y - from.Y) * fraction);
    }

    private Point PointAtRaw(int index) => _path[index];



    /// <summary>The head's position along the lap at a linear time fraction, 0..1.</summary>
    internal static double EaseProgress(double progress)
        => CubicBezier(Math.Clamp(progress, 0, 1));

    /// <summary>Tail length at a linear time fraction. The same Bezier's derivative is the
    /// head speed, so the trail contracts while the head eases in and out and stretches on
    /// the fast middle of the lap.</summary>
    internal static double TrailLengthAt(double progress, double baseTrailLength)
    {
        double p = Math.Clamp(progress, 0, 1);
        double speed = Math.Max(0, (CubicBezier(Math.Min(1, p + 0.01))
            - CubicBezier(Math.Max(0, p - 0.01))) / 0.02);
        double speedRatio = Math.Clamp(speed / PeakSpeed, 0, 1);
        return baseTrailLength * (0.35 + 0.65 * speedRatio);
    }

    private static double CubicBezier(double x)
    {
        double low = 0;
        double high = 1;
        double t = x;
        for (int iteration = 0; iteration < 18; iteration++)
        {
            double estimate = Bezier(t, EaseX1, EaseX2) - x;
            if (Math.Abs(estimate) < 1e-6)
            {
                break;
            }

            if (estimate < 0)
            {
                low = t;
            }
            else
            {
                high = t;
            }

            t = (low + high) / 2;
        }

        return Bezier(t, EaseY1, EaseY2);
    }

    private static double Bezier(double t, double control1, double control2)
    {
        double inverse = 1 - t;
        return 3 * inverse * inverse * t * control1
            + 3 * inverse * t * t * control2
            + t * t * t;
    }
}
