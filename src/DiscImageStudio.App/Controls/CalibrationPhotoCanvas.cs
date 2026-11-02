using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;

namespace DiscImageStudio.Controls;

/// <summary>Photo boundaries use source pixels; corrected-photo traces use disc-centred
/// millimetres, Y down. Wheel zooms, right/middle-drag (or Space + left-drag) pans; left
/// drags control points, left-click on the corrected photo adds a trace point and Delete
/// removes the selected one.</summary>
public sealed class CalibrationPhotoCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<Bitmap?> FallbackImageProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, Bitmap?>(nameof(FallbackImage));
    public static readonly StyledProperty<bool> IsPhotoStepProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsPhotoStep), true);
    public static readonly StyledProperty<bool> IsRectificationPreviewProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsRectificationPreview));
    public static readonly StyledProperty<bool> IsTargetPreviewProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsTargetPreview));
    public static readonly StyledProperty<double> CanvasRadiusMmProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, double>(nameof(CanvasRadiusMm), 63.0);
    public static readonly StyledProperty<double> TargetCanvasRadiusMmProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, double>(nameof(TargetCanvasRadiusMm), 60.0);
    public static readonly StyledProperty<IEnumerable<CalibrationPhotoBoundaryPoint>?> BoundaryPointsProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, IEnumerable<CalibrationPhotoBoundaryPoint>?>(nameof(BoundaryPoints));
    public static readonly StyledProperty<IReadOnlyList<PlanePoint[]>?> BoundaryContoursProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<PlanePoint[]>?>(nameof(BoundaryContours));
    public static readonly StyledProperty<IEnumerable<CalibrationTracePoint>?> TracePointsProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, IEnumerable<CalibrationTracePoint>?>(nameof(TracePoints));
    public static readonly StyledProperty<IReadOnlyList<PlanePoint[]>?> PredictionContoursProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<PlanePoint[]>?>(nameof(PredictionContours));
    public static readonly StyledProperty<IReadOnlyList<int>?> OutlierTracePointIndicesProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<int>?>(nameof(OutlierTracePointIndices));
    public static readonly StyledProperty<int> SelectedTraceArmProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, int>(nameof(SelectedTraceArm), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<ICommand?> BoundaryEditedCommandProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(BoundaryEditedCommand));
    public static readonly StyledProperty<ICommand?> TraceEditedCommandProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(TraceEditedCommand));
    public static readonly StyledProperty<bool> ShowPredictionProperty = AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(ShowPrediction), true);
    public static readonly DirectProperty<CalibrationPhotoCanvas, string> ViewDescriptionProperty =
        AvaloniaProperty.RegisterDirect<CalibrationPhotoCanvas, string>(nameof(ViewDescription), canvas => canvas.ViewDescription);
    public static readonly DirectProperty<CalibrationPhotoCanvas, string> SelectionDescriptionProperty =
        AvaloniaProperty.RegisterDirect<CalibrationPhotoCanvas, string>(nameof(SelectionDescription), canvas => canvas.SelectionDescription);

    private static readonly AvaloniaProperty[] RenderProperties =
    [
        ImageProperty, FallbackImageProperty, IsPhotoStepProperty, IsRectificationPreviewProperty, IsTargetPreviewProperty,
        CanvasRadiusMmProperty, TargetCanvasRadiusMmProperty, BoundaryPointsProperty, BoundaryContoursProperty,
        TracePointsProperty, PredictionContoursProperty, OutlierTracePointIndicesProperty, SelectedTraceArmProperty,
        ShowPredictionProperty,
    ];

    private static readonly AvaloniaProperty[] ViewportProperties =
    [
        ImageProperty, IsPhotoStepProperty, IsRectificationPreviewProperty, IsTargetPreviewProperty, CanvasRadiusMmProperty,
    ];

    private string _viewDescription = "100%";
    private string _selectionDescription = "滚轮缩放，右键平移；拖动控制点贴合边缘。";
    private double _zoom = 1;
    private Vector _pan;
    private bool _panning;
    private bool _spaceDown;
    private Point _panStart;
    private Vector _panBefore;
    private CalibrationPhotoBoundaryPoint? _boundaryDrag;
    private CalibrationTracePoint? _traceDrag;
    private Point _dragStart;
    private Point _dragOriginal;
    private Point _dragPosition;
    private bool _hasMoved;
    private (int Arm, int Index)? _selectedTrace;
    private (CalibrationPhotoBoundaryKind Boundary, int Index)? _selectedBoundary;

    public CalibrationPhotoCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public Bitmap? FallbackImage { get => GetValue(FallbackImageProperty); set => SetValue(FallbackImageProperty, value); }
    public bool IsPhotoStep { get => GetValue(IsPhotoStepProperty); set => SetValue(IsPhotoStepProperty, value); }
    public bool IsRectificationPreview { get => GetValue(IsRectificationPreviewProperty); set => SetValue(IsRectificationPreviewProperty, value); }
    public bool IsTargetPreview { get => GetValue(IsTargetPreviewProperty); set => SetValue(IsTargetPreviewProperty, value); }
    public double CanvasRadiusMm { get => GetValue(CanvasRadiusMmProperty); set => SetValue(CanvasRadiusMmProperty, value); }
    public double TargetCanvasRadiusMm { get => GetValue(TargetCanvasRadiusMmProperty); set => SetValue(TargetCanvasRadiusMmProperty, value); }
    public IEnumerable<CalibrationPhotoBoundaryPoint>? BoundaryPoints { get => GetValue(BoundaryPointsProperty); set => SetValue(BoundaryPointsProperty, value); }
    public IReadOnlyList<PlanePoint[]>? BoundaryContours { get => GetValue(BoundaryContoursProperty); set => SetValue(BoundaryContoursProperty, value); }
    public IEnumerable<CalibrationTracePoint>? TracePoints { get => GetValue(TracePointsProperty); set => SetValue(TracePointsProperty, value); }
    public IReadOnlyList<PlanePoint[]>? PredictionContours { get => GetValue(PredictionContoursProperty); set => SetValue(PredictionContoursProperty, value); }
    public IReadOnlyList<int>? OutlierTracePointIndices { get => GetValue(OutlierTracePointIndicesProperty); set => SetValue(OutlierTracePointIndicesProperty, value); }
    public int SelectedTraceArm { get => GetValue(SelectedTraceArmProperty); set => SetValue(SelectedTraceArmProperty, value); }
    public ICommand? BoundaryEditedCommand { get => GetValue(BoundaryEditedCommandProperty); set => SetValue(BoundaryEditedCommandProperty, value); }
    public ICommand? TraceEditedCommand { get => GetValue(TraceEditedCommandProperty); set => SetValue(TraceEditedCommandProperty, value); }
    public bool ShowPrediction { get => GetValue(ShowPredictionProperty); set => SetValue(ShowPredictionProperty, value); }

    public string ViewDescription
    {
        get => _viewDescription;
        private set => SetAndRaise(ViewDescriptionProperty, ref _viewDescription, value);
    }

    public string SelectionDescription
    {
        get => _selectionDescription;
        private set => SetAndRaise(SelectionDescriptionProperty, ref _selectionDescription, value);
    }

    private bool ShowingTarget => IsTargetPreview || Image is null;

    private bool ShowingRawPhoto => !ShowingTarget && IsPhotoStep && !IsRectificationPreview;

    private bool CanEditTrace => !ShowingTarget && !IsPhotoStep && !IsRectificationPreview;

    private bool Dragging => _boundaryDrag is not null || _traceDrag is not null;

    private Rect WorldBounds
    {
        get
        {
            if (ShowingRawPhoto && Image is { } photo) return new Rect(-.5, -.5, photo.PixelSize.Width, photo.PixelSize.Height);
            double radius = ShowingTarget ? TargetCanvasRadiusMm : CanvasRadiusMm;
            radius = double.IsFinite(radius) && radius > 0 ? radius : 63;
            return new Rect(-radius, -radius, radius * 2, radius * 2);
        }
    }

    private Matrix ViewMatrix
    {
        get
        {
            Rect bounds = WorldBounds;
            double scale = Math.Max(.0001, Math.Min(Bounds.Width / bounds.Width, Bounds.Height / bounds.Height) * .92 * _zoom);
            return Matrix.CreateTranslation(-bounds.X - bounds.Width / 2, -bounds.Y - bounds.Height / 2)
                * Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation(Bounds.Width / 2 + _pan.X, Bounds.Height / 2 + _pan.Y);
        }
    }

    public Point WorldToView(Point point) => ViewMatrix.Transform(point);

    // Inverted as an affine map: Matrix.TryInvert can leave M33 a hair off 1, which sends
    // Matrix.Transform down its single-precision perspective path (~1e-5 px of drift per edit).
    public Point ViewToWorld(Point point)
    {
        Matrix m = ViewMatrix;
        double determinant = m.M11 * m.M22 - m.M12 * m.M21;
        if (Math.Abs(determinant) < 1e-18) return point;
        double x = point.X - m.M31, y = point.Y - m.M32;
        return new Point((x * m.M22 - y * m.M21) / determinant, (y * m.M11 - x * m.M12) / determinant);
    }

    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
        ViewDescription = "100%";
        InvalidateVisual();
    }

    public void PanBy(Vector pixels)
    {
        _pan += pixels;
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => ZoomAt(factor, new Point(Bounds.Width / 2, Bounds.Height / 2));

    private void ZoomAt(double factor, Point screen)
    {
        if (!double.IsFinite(factor) || factor <= 0 || Dragging) return;
        Point anchor = ViewToWorld(screen);
        _zoom = Math.Clamp(_zoom * factor, .5, 30);
        _pan += screen - WorldToView(anchor);
        ViewDescription = $"{_zoom * 100:0}%";
        InvalidateVisual();
    }

    public bool BeginDragAt(Point screen)
    {
        if (ShowingRawPhoto)
        {
            CalibrationPhotoBoundaryPoint? point = BoundaryPoints?
                .OrderBy(p => Distance(WorldToView(new Point(p.X, p.Y)), screen))
                .FirstOrDefault(p => Distance(WorldToView(new Point(p.X, p.Y)), screen) <= 11);
            if (point is null) return false;
            _boundaryDrag = point;
            _selectedBoundary = (point.Boundary, point.Index);
            _dragOriginal = new Point(point.X, point.Y);
            SelectionDescription = point.Boundary == CalibrationPhotoBoundaryKind.OuterEdge
                ? "拖到盘片实体外缘；不要沿刻录区边界。" : "拖到实体内缘；不要沿透明塑料环。";
        }
        else if (CanEditTrace)
        {
            CalibrationTracePoint? point = TracePoints?
                .OrderBy(p => Distance(WorldToView(Position(p)), screen))
                .ThenBy(p => p.Arm == SelectedTraceArm ? 0 : 1)
                .FirstOrDefault(p => Distance(WorldToView(Position(p)), screen) <= 10);
            if (point is null) return false;
            _traceDrag = point;
            _selectedTrace = (point.Arm, point.Index);
            SetCurrentValue(SelectedTraceArmProperty, point.Arm);
            _dragOriginal = Position(point);
            UpdateTraceDescription();
        }
        else
        {
            return false;
        }

        _dragStart = ViewToWorld(screen);
        _dragPosition = _dragOriginal;
        _hasMoved = false;
        InvalidateVisual();
        return true;
    }

    public void DragTo(Point screen)
    {
        if (!Dragging) return;
        _dragPosition = _dragOriginal + (ViewToWorld(screen) - _dragStart);
        Rect bounds = ShowingRawPhoto && Image is { } photo
            ? new Rect(0, 0, photo.PixelSize.Width - 1, photo.PixelSize.Height - 1) : WorldBounds;
        _dragPosition = new Point(Math.Clamp(_dragPosition.X, bounds.Left, bounds.Right), Math.Clamp(_dragPosition.Y, bounds.Top, bounds.Bottom));
        _hasMoved |= Distance(_dragPosition, _dragOriginal) > 1e-8;
        InvalidateVisual();
    }

    public void CompleteDrag(bool commit = true)
    {
        CalibrationPhotoBoundaryPoint? boundary = _boundaryDrag;
        CalibrationTracePoint? trace = _traceDrag;
        Point position = _dragPosition;
        bool changed = _hasMoved && commit;
        _boundaryDrag = null;
        _traceDrag = null;
        _hasMoved = false;
        if (changed && boundary is not null)
            Execute(BoundaryEditedCommand, new CalibrationBoundaryEdit(boundary.Boundary, boundary.Index, position.X, position.Y));
        if (changed && trace is not null)
        {
            Execute(TraceEditedCommand, new CalibrationTraceEdit(trace.Arm, trace.Index, position.X, position.Y));
            SelectTraceNear(trace.Arm, position);
        }

        InvalidateVisual();
    }

    public bool AddTraceAt(Point screen)
    {
        if (!CanEditTrace || Dragging) return false;
        Point point = ViewToWorld(screen);
        if (!WorldBounds.Contains(point) || SelectedTraceArm is < 0 or > 3) return false;
        CalibrationTraceEdit edit = new(SelectedTraceArm, -1, point.X, point.Y);
        if (TraceEditedCommand?.CanExecute(edit) != true) return false;
        TraceEditedCommand.Execute(edit);
        SelectTraceNear(SelectedTraceArm, point);
        InvalidateVisual();
        return true;
    }

    public void RemoveSelectedTracePoint()
    {
        if (!CanEditTrace || Dragging || _selectedTrace is not { } selected) return;
        CalibrationTracePoint? point = TracePoints?.FirstOrDefault(p => p.Arm == selected.Arm && p.Index == selected.Index);
        if (point is null) return;
        CalibrationTraceEdit edit = new(point.Arm, point.Index, point.XMm, point.YMm, true);
        if (TraceEditedCommand?.CanExecute(edit) != true) return;
        TraceEditedCommand.Execute(edit);
        _selectedTrace = null;
        SelectionDescription = "已删除此点；点击照片继续描当前曲线。";
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        dc.DrawRectangle(DiscTheme.PhotoBackdrop, null, new Rect(Bounds.Size));
        Bitmap? bitmap = ShowingTarget ? FallbackImage : Image;
        Rect bounds = WorldBounds;
        Rect viewBounds = new(WorldToView(bounds.TopLeft), WorldToView(bounds.BottomRight));
        if (bitmap is not null) dc.DrawImage(bitmap, viewBounds);
        else DrawPlaceholder(dc, bounds);
        if (ShowingRawPhoto)
        {
            DrawBoundaries(dc);
        }
        else if (CanEditTrace)
        {
            if (ShowPrediction && PredictionContours is { } predictions)
                foreach (PlanePoint[] prediction in predictions) DrawPath(dc, ToPoints(prediction), new Pen(DiscTheme.Prediction, 1.8), false);
            for (int arm = 0; arm < 4; arm++)
            {
                // The VM orders samples along the curve; Index is only an editing identity.
                CalibrationTracePoint[] points = TracePoints?.Where(p => p.Arm == arm).ToArray() ?? [];
                IBrush brush = DiscTheme.Trace;
                // Square caps as WPF's default DashCap: a dot pattern has zero-length dashes,
                // which flat caps would not draw at all.
                DrawPath(dc, points.Select(TraceDisplayPosition).ToArray(), new Pen(brush, 1.2) { DashStyle = DashStyle.Dot, LineCap = PenLineCap.Square }, false);
                foreach (CalibrationTracePoint point in points)
                {
                    bool selected = _selectedTrace == (point.Arm, point.Index);
                    bool outlier = OutlierTracePointIndices?.Contains(point.Index) == true;
                    IBrush pointBrush = outlier ? DiscTheme.Outlier : brush;
                    Point screen = WorldToView(TraceDisplayPosition(point));
                    double radius = selected ? 6 : 4;
                    dc.DrawEllipse(pointBrush, new Pen(DiscTheme.Outline, 1.4), screen, radius, radius);
                    if (selected) DrawText(dc, $"点 {point.Index + 1}", screen + new Vector(9, -16), pointBrush, 12);
                }
            }
        }

        if (ShowingTarget)
            DrawCaption(dc, Image is null ? "标靶预览 · 导入照片后开始矫正" : "刻录图案预览 · 关闭预览后继续照片标定");
        else if (IsRectificationPreview)
            DrawCaption(dc, "矫正预览 · 确认后开始描线");
    }

    private static Point[] ToPoints(PlanePoint[] points) => Array.ConvertAll(points, point => new Point(point.X, point.Y));

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void DrawBoundaries(DrawingContext dc)
    {
        foreach (CalibrationPhotoBoundaryKind kind in new[] { CalibrationPhotoBoundaryKind.OuterEdge, CalibrationPhotoBoundaryKind.CenterHole })
        {
            IBrush brush = kind == CalibrationPhotoBoundaryKind.OuterEdge ? DiscTheme.Edge : DiscTheme.HubOutline;
            CalibrationPhotoBoundaryPoint[] points = BoundaryPoints?.Where(p => p.Boundary == kind).OrderBy(p => p.Index).ToArray() ?? [];
            PlanePoint[]? outline = BoundaryContours?.ElementAtOrDefault((int)kind);
            if (_boundaryDrag?.Boundary == kind || outline is not { Length: > 2 })
                DrawPath(dc, points.Select(BoundaryDisplayPosition).ToArray(), new Pen(brush, 1.3) { DashStyle = DashStyle.Dash, LineCap = PenLineCap.Square }, true);
            else
                DrawPath(dc, ToPoints(outline), new Pen(brush, 1.6), true);
            foreach (CalibrationPhotoBoundaryPoint point in points)
            {
                bool selected = _selectedBoundary == (point.Boundary, point.Index);
                double radius = selected ? 6 : 4.5;
                dc.DrawEllipse(brush, new Pen(DiscTheme.Outline, 1.2), WorldToView(BoundaryDisplayPosition(point)), radius, radius);
            }
        }
    }

    private void DrawPlaceholder(DrawingContext dc, Rect bounds)
    {
        Point center = WorldToView(default);
        double radius = Math.Min(bounds.Width, bounds.Height) * .44;
        Pen pen = new(DiscTheme.Handle, 1.5);
        for (int i = 1; i <= 5; i++)
        {
            double viewRadius = Distance(WorldToView(new Point(radius * i / 5, 0)), center);
            dc.DrawEllipse(null, pen, center, viewRadius, viewRadius);
        }

        DrawPath(dc, [new(-radius, 0), new(radius, 0)], pen, false);
        DrawPath(dc, [new(0, -radius), new(0, radius)], pen, false);
    }

    private void DrawPath(DrawingContext dc, IReadOnlyList<Point> points, Pen pen, bool closed)
    {
        if (points.Count < 2) return;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            bool started = false;
            foreach (Point point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                {
                    if (started) context.EndFigure(false);
                    started = false;
                    continue;
                }

                Point screen = WorldToView(point);
                if (!started)
                {
                    context.BeginFigure(screen, false);
                    started = true;
                }
                else
                {
                    context.LineTo(screen);
                }
            }

            if (started) context.EndFigure(closed);
        }

        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawCaption(DrawingContext dc, string text)
    {
        FormattedText label = Text(text, 11, DiscTheme.Handle);
        Rect bounds = new(8, Math.Max(4, Bounds.Height - label.Height - 12), label.Width + 12, label.Height + 6);
        dc.DrawRectangle(DiscTheme.Caption, null, bounds, 4, 4);
        dc.DrawText(label, new Point(bounds.Left + 6, bounds.Top + 3));
    }

    private static void DrawText(DrawingContext dc, string text, Point at, IBrush brush, double size)
        => dc.DrawText(Text(text, size, brush), at);

    private static FormattedText Text(string text, double size, IBrush brush)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, DiscTheme.Typeface, size, brush);

    private Point BoundaryDisplayPosition(CalibrationPhotoBoundaryPoint point)
        => _boundaryDrag?.Boundary == point.Boundary && _boundaryDrag.Index == point.Index ? _dragPosition : new Point(point.X, point.Y);

    private Point TraceDisplayPosition(CalibrationTracePoint point)
        => _traceDrag?.Arm == point.Arm && _traceDrag.Index == point.Index ? _dragPosition : Position(point);

    private static Point Position(CalibrationTracePoint point) => new(point.XMm, point.YMm);

    private static void Execute(ICommand? command, object edit)
    {
        if (command?.CanExecute(edit) == true) command.Execute(edit);
    }

    private void SelectTraceNear(int arm, Point position)
    {
        CalibrationTracePoint? point = TracePoints?.Where(p => p.Arm == arm).MinBy(p => Distance(Position(p), position));
        _selectedTrace = point is null || Distance(Position(point), position) > 1e-6 ? null : (point.Arm, point.Index);
        UpdateTraceDescription();
    }

    private void UpdateTraceDescription() => SelectionDescription = _selectedTrace is { } point
        ? $"已选曲线上的点 {point.Index + 1} · 直接拖动修正，Delete 删除"
        : "点击照片上同一条可见曲线，由内向外连续描点。";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundaryPointsProperty || change.Property == TracePointsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= PointsChanged;
            if (change.NewValue is INotifyCollectionChanged current) current.CollectionChanged += PointsChanged;
        }

        if (Array.IndexOf(ViewportProperties, change.Property) >= 0)
        {
            CompleteDrag(false);
            _selectedTrace = null;
            _selectedBoundary = null;
            ResetView();
            SelectionDescription = IsPhotoStep
                ? "滚轮缩放，右键平移；拖动控制点贴合边缘。"
                : "滚轮缩放，右键平移；点击添加点，拖动已有点修正位置。";
        }

        if (Array.IndexOf(RenderProperties, change.Property) >= 0) InvalidateVisual();
    }

    private void PointsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point position = e.GetPosition(this);
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed || (properties.IsLeftButtonPressed && _spaceDown))
        {
            _panning = true;
            _panStart = position;
            _panBefore = _pan;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed)
        {
            if (BeginDragAt(position)) e.Pointer.Capture(this);
            else AddTraceAt(position);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        if (_panning)
        {
            _pan = _panBefore + (position - _panStart);
            InvalidateVisual();
        }
        else if (Dragging)
        {
            DragTo(position);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }
        else if (Dragging)
        {
            CompleteDrag();
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panning = false;
        if (Dragging) CompleteDrag(false);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        ZoomAt(e.Delta.Y > 0 ? 1.2 : 1 / 1.2, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = true;
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            RemoveSelectedTracePoint();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CompleteDrag(false);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space) _spaceDown = false;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _spaceDown = false;
    }
}
