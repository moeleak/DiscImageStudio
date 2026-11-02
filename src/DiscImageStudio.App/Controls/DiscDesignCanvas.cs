using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DiscImageStudio.Hosting;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Controls;

/// <summary>
/// The design page's stage: the disc drawn in millimetres around its centre, the layer stack
/// painted bottom-up, a dim mask outside the content ring and the selected layer's handles.
/// Drag moves a layer, corner handles scale it, the top handle rotates it (Shift snaps to
/// 90°). Ctrl/Alt (Cmd on macOS) + wheel zooms about the pointer, middle-drag pans while zoomed,
/// and double-clicking empty space resets the view.
/// </summary>
public class DiscDesignCanvas : Control
{
    private const double ViewMarginMm = 3.0;
    private const double HandleSize = 8.0;
    private const double RotateHandleDistance = 26.0;
    private const double MinScale = 0.05;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 12.0;
    private const double ZoomStep = 1.25;
    private const double WheelZoomStep = 1.15;

    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<DiscDesignCanvas, double>(nameof(ZoomFactor), 1.0,
            coerce: static (_, value) => Math.Clamp(value, MinZoom, MaxZoom));

    public static readonly DirectProperty<DiscDesignCanvas, string> ZoomPercentProperty =
        AvaloniaProperty.RegisterDirect<DiscDesignCanvas, string>(nameof(ZoomPercent), canvas => canvas.ZoomPercent);

    private DesignViewModel? _design;
    private DragMode _dragMode;
    private Point _dragStartMm;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;
    private double _dragStartScale = 1.0;
    private double _dragStartDistanceMm = 1.0;
    private double _dragStartRotationDeg;
    private double _dragStartMouseAngleDeg;
    private bool _isDragging;
    private bool _isPanning;
    private Vector _panOffset;
    private Vector _panStartOffset;
    private Point _panStartMouse;
    private string _zoomPercent = "100%";

    private enum DragMode
    {
        None,
        Move,
        Scale,
        Rotate,
    }

    public DiscDesignCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        DataContextChanged += (_, _) => AttachDesign();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        DetachedFromVisualTree += (_, _) => DetachDesign();
        AttachedToVisualTree += (_, _) => AttachDesign();
    }

    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public string ZoomPercent
    {
        get => _zoomPercent;
        private set => SetAndRaise(ZoomPercentProperty, ref _zoomPercent, value);
    }

    public void ResetView()
    {
        ZoomFactor = 1.0;
        _panOffset = default;
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAt(ZoomStep, ViewCenter);

    public void ZoomOut() => ZoomAt(1.0 / ZoomStep, ViewCenter);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoomFactorProperty)
        {
            ZoomPercent = $"{Math.Round(ZoomFactor * 100)}%";
            InvalidateVisual();
        }
    }

    private void AttachDesign()
    {
        if (ReferenceEquals(_design, DataContext)) return;
        DetachDesign();
        if (DataContext is not DesignViewModel design) return;
        _design = design;
        design.CanvasChanged += InvalidateVisual;
        design.Layers.CollectionChanged += OnLayersChanged;
        foreach (DesignLayerViewModel layer in design.Layers) layer.PropertyChanged += OnLayerPropertyChanged;
        InvalidateVisual();
    }

    private void DetachDesign()
    {
        if (_design is null) return;
        _design.CanvasChanged -= InvalidateVisual;
        _design.Layers.CollectionChanged -= OnLayersChanged;
        foreach (DesignLayerViewModel layer in _design.Layers) layer.PropertyChanged -= OnLayerPropertyChanged;
        _design = null;
    }

    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        InvalidateVisual();
        if (args.OldItems is not null)
            foreach (DesignLayerViewModel layer in args.OldItems) layer.PropertyChanged -= OnLayerPropertyChanged;
        if (args.NewItems is not null)
            foreach (DesignLayerViewModel layer in args.NewItems) layer.PropertyChanged += OnLayerPropertyChanged;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private double Width0 => Bounds.Width;

    private double Height0 => Bounds.Height;

    private double FitScale
    {
        get
        {
            double radiusMm = DesignViewModel.DiscRadiusMm + ViewMarginMm;
            return Math.Min(Width0 / (2 * radiusMm), Height0 / (2 * radiusMm));
        }
    }

    private double ViewScale => FitScale * ZoomFactor;

    private Point ViewCenterBase => new(Width0 / 2, Height0 / 2);

    private Point ViewCenter => ViewCenterBase + _panOffset;

    private Point ScreenFromMm(double xMm, double yMm)
    {
        Point center = ViewCenter;
        double scale = ViewScale;
        return new Point(center.X + xMm * scale, center.Y + yMm * scale);
    }

    private Point MmFromScreen(Point point)
    {
        Point center = ViewCenter;
        double scale = ViewScale;
        return new Point((point.X - center.X) / scale, (point.Y - center.Y) / scale);
    }

    private double ScreenLength(double mm) => mm * ViewScale;

    private static double SizeWidthMm(DesignLayerViewModel layer) => layer.BaseWidthMm * layer.Scale;

    private static double SizeHeightMm(DesignLayerViewModel layer) => layer.BaseHeightMm * layer.Scale;

    private static Point[] LayerCorners(DesignLayerViewModel layer)
    {
        double halfWidth = SizeWidthMm(layer) / 2;
        double halfHeight = SizeHeightMm(layer) / 2;
        double radians = layer.RotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        (double dx, double dy)[] locals =
        [
            (-halfWidth, -halfHeight),
            (halfWidth, -halfHeight),
            (halfWidth, halfHeight),
            (-halfWidth, halfHeight),
        ];
        return locals
            .Select(local => new Point(
                layer.TranslateXMm + local.dx * cos - local.dy * sin,
                layer.TranslateYMm + local.dx * sin + local.dy * cos))
            .ToArray();
    }

    private static Geometry PolygonGeometry(Point[] points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false);
            for (int i = 1; i < points.Length; i++) context.LineTo(points[i]);
            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static bool PointInPolygon(Point point, Point[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            bool crosses = (polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y)
                    + polygon[i].X;
            if (crosses) inside = !inside;
        }

        return inside;
    }

    public override void Render(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(DiscTheme.Backdrop, null, new Rect(0, 0, Width0, Height0));
        if (_design is null || Width0 <= 0 || Height0 <= 0) return;
        double discRadius = ScreenLength(DesignViewModel.DiscRadiusMm);
        double hubRadius = ScreenLength(DesignViewModel.HubRadiusMm);
        double innerRadius = ScreenLength(_design.ContentInnerRadiusMm);
        double outerRadius = ScreenLength(Math.Min(_design.ContentOuterRadiusMm, DesignViewModel.DiscRadiusMm));
        Point center = ViewCenter;
        DiscRenderCore.DrawDiscBody(drawingContext, center, discRadius, hubRadius);
        DiscRenderCore.DrawGuideCircles(drawingContext, center, discRadius, outerRadius, innerRadius);
        DiscRenderCore.DrawOriginCross(drawingContext, center, 5);
        // Layers are stored topmost-first, so painting runs bottom-up.
        for (int index = _design.Layers.Count - 1; index >= 0; index--) DrawLayer(drawingContext, _design.Layers[index]);
        DrawDimMask(drawingContext, center, outerRadius, innerRadius);
        if (_design.SelectedLayer is { } selected) DrawSelection(drawingContext, selected);
        if (!_design.HasLayers) DrawEmptyHint(drawingContext, center);
    }

    private void DrawLayer(DrawingContext context, DesignLayerViewModel layer)
    {
        double widthPx = ScreenLength(SizeWidthMm(layer));
        double heightPx = ScreenLength(SizeHeightMm(layer));
        if (widthPx <= 0 || heightPx <= 0) return;
        Point topLeft = ScreenFromMm(layer.TranslateXMm - SizeWidthMm(layer) / 2, layer.TranslateYMm - SizeHeightMm(layer) / 2);
        Point[] screenCorners() => LayerCorners(layer).Select(corner => ScreenFromMm(corner.X, corner.Y)).ToArray();
        if (!layer.IsVisible)
        {
            context.DrawGeometry(null, new Pen(DiscTheme.Guide, 1) { DashStyle = DashStyle.Dash, LineCap = PenLineCap.Square }, PolygonGeometry(screenCorners()));
            return;
        }

        if (layer.HasImage && AvaloniaBitmaps.ToBitmap(layer.Image) is { } bitmap)
        {
            // Rotate about the layer centre, after placing its top-left corner.
            Point layerCenter = new(topLeft.X + widthPx / 2, topLeft.Y + heightPx / 2);
            Matrix transform = Matrix.CreateTranslation(topLeft.X, topLeft.Y)
                * Matrix.CreateTranslation(-layerCenter.X, -layerCenter.Y)
                * Matrix.CreateRotation(layer.RotationDeg * Math.PI / 180.0)
                * Matrix.CreateTranslation(layerCenter.X, layerCenter.Y);
            using (context.PushTransform(transform))
            {
                context.DrawImage(bitmap, new Rect(0, 0, widthPx, heightPx));
            }
        }
        else
        {
            context.DrawGeometry(null, new Pen(DiscTheme.MissingImage, 1) { DashStyle = DashStyle.Dash, LineCap = PenLineCap.Square }, PolygonGeometry(screenCorners()));
        }
    }

    private void DrawDimMask(DrawingContext context, Point center, double outerRadius, double innerRadius)
    {
        PathGeometry geometry = new() { FillRule = FillRule.EvenOdd, Figures = [] };
        geometry.Figures.Add(DiscRenderCore.RectangleFigure(new Rect(0, 0, Width0, Height0)));
        geometry.Figures.Add(DiscRenderCore.CircleFigure(center, outerRadius));
        geometry.Figures.Add(DiscRenderCore.CircleFigure(center, innerRadius));
        context.DrawGeometry(DiscTheme.Mask, null, geometry);
    }

    private void DrawSelection(DrawingContext context, DesignLayerViewModel layer)
    {
        Point[] cornersMm = LayerCorners(layer);
        Point[] corners = cornersMm.Select(corner => ScreenFromMm(corner.X, corner.Y)).ToArray();
        Pen outlinePen = new(DiscTheme.Selection, 1.5);
        context.DrawGeometry(null, outlinePen, PolygonGeometry(corners));
        foreach (Point corner in corners)
        {
            Rect handle = new(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize);
            context.DrawRectangle(Brushes.White, new Pen(DiscTheme.Selection, 1.5), handle);
        }

        Point topMidMm = new((cornersMm[0].X + cornersMm[1].X) / 2, (cornersMm[0].Y + cornersMm[1].Y) / 2);
        double dx = topMidMm.X - layer.TranslateXMm;
        double dy = topMidMm.Y - layer.TranslateYMm;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
        {
            dx = 0;
            dy = -1;
            length = 1;
        }

        double extensionMm = RotateHandleDistance / Math.Max(0.001, ViewScale);
        Point handleMm = new(topMidMm.X + dx / length * extensionMm, topMidMm.Y + dy / length * extensionMm);
        Point handleTop = ScreenFromMm(topMidMm.X, topMidMm.Y);
        Point handlePoint = ScreenFromMm(handleMm.X, handleMm.Y);
        context.DrawLine(outlinePen, handleTop, handlePoint);
        context.DrawEllipse(Brushes.White, new Pen(DiscTheme.Selection, 1.5), handlePoint, HandleSize / 2, HandleSize / 2);
    }

    private void DrawEmptyHint(DrawingContext context, Point center)
    {
        FormattedText text = DiscRenderCore.CreateText("点击左侧「导入图片」开始设计", 14, DiscTheme.Guide);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static bool Near(Point a, Point b, double tolerance)
        => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private int? HitScaleHandle(Point mouse)
    {
        if (_design?.SelectedLayer is not { } layer) return null;
        Point[] cornersMm = LayerCorners(layer);
        for (int i = 0; i < cornersMm.Length; i++)
        {
            if (Near(mouse, ScreenFromMm(cornersMm[i].X, cornersMm[i].Y), HandleSize)) return i;
        }

        return null;
    }

    private Point? RotateHandlePoint(DesignLayerViewModel layer)
    {
        Point[] cornersMm = LayerCorners(layer);
        double dx = (cornersMm[0].X + cornersMm[1].X) / 2 - layer.TranslateXMm;
        double dy = (cornersMm[0].Y + cornersMm[1].Y) / 2 - layer.TranslateYMm;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return null;
        double extensionMm = RotateHandleDistance / Math.Max(0.001, ViewScale);
        return ScreenFromMm(layer.TranslateXMm + dx / length * (length + extensionMm),
            layer.TranslateYMm + dy / length * (length + extensionMm));
    }

    private DesignLayerViewModel? HitTestLayer(Point mouseMm)
    {
        if (_design is null) return null;
        DesignLayerViewModel? hidden = null;
        foreach (DesignLayerViewModel layer in _design.Layers)
        {
            if (!PointInPolygon(mouseMm, LayerCorners(layer))) continue;
            if (layer.IsVisible) return layer;
            hidden ??= layer;
        }

        return hidden;
    }

    private static bool IsZoomModifier(KeyModifiers modifiers)
        => (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_design is null || !IsZoomModifier(e.KeyModifiers) || e.Delta.Y == 0) return;
        ZoomAt(e.Delta.Y > 0 ? WheelZoomStep : 1.0 / WheelZoomStep, e.GetPosition(this));
        e.Handled = true;
    }

    internal void ZoomAt(double factor, Point anchor)
    {
        double fit = FitScale;
        if (fit <= 0 || Width0 <= 0 || Height0 <= 0) return;
        Point anchorMm = MmFromScreen(anchor);
        ZoomFactor *= factor;
        _panOffset = ZoomFactor > 1.0001
            ? ClampPan(anchor - ViewCenterBase - (Vector)anchorMm * (fit * ZoomFactor))
            : default;
        InvalidateVisual();
    }

    private Vector ClampPan(Vector pan)
    {
        double fit = FitScale;
        if (fit <= 0 || ZoomFactor <= 1.0001) return default;
        double maxLength = fit * ZoomFactor * DesignViewModel.DiscRadiusMm;
        return pan.Length > maxLength ? pan * (maxLength / pan.Length) : pan;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_design is null) return;
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        Point mouse = e.GetPosition(this);
        if (properties.IsMiddleButtonPressed)
        {
            if (ZoomFactor <= 1.0001) return;
            _isPanning = true;
            _panStartOffset = _panOffset;
            _panStartMouse = mouse;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;
        Focus();
        Point mouseMm = MmFromScreen(mouse);
        if (e.ClickCount == 2 && _design.SelectedLayer is null && HitScaleHandle(mouse) is null && HitTestLayer(mouseMm) is null)
        {
            ResetView();
            return;
        }

        if (HitScaleHandle(mouse) is not null)
        {
            DesignLayerViewModel scaleLayer = _design.SelectedLayer!;
            _dragMode = DragMode.Scale;
            _dragStartMm = mouseMm;
            _dragStartScale = scaleLayer.Scale;
            double dx = mouseMm.X - scaleLayer.TranslateXMm;
            double dy = mouseMm.Y - scaleLayer.TranslateYMm;
            _dragStartDistanceMm = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
            BeginDrag(e);
            return;
        }

        if (_design.SelectedLayer is { } selected && RotateHandlePoint(selected) is { } rotatePoint
            && Near(mouse, rotatePoint, HandleSize + 2))
        {
            _dragMode = DragMode.Rotate;
            _dragStartRotationDeg = selected.RotationDeg;
            _dragStartMouseAngleDeg = AngleFromCenter(selected, mouseMm);
            BeginDrag(e);
            return;
        }

        if (HitTestLayer(mouseMm) is { } hit)
        {
            _design.SelectedLayer = hit;
            _dragMode = DragMode.Move;
            _dragStartMm = mouseMm;
            _dragStartTranslateX = hit.TranslateXMm;
            _dragStartTranslateY = hit.TranslateYMm;
            BeginDrag(e);
            return;
        }

        _design.SelectedLayer = null;
    }

    private void BeginDrag(PointerPressedEventArgs e)
    {
        _isDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private static double AngleFromCenter(DesignLayerViewModel layer, Point mouseMm)
        => Math.Atan2(mouseMm.Y - layer.TranslateYMm, mouseMm.X - layer.TranslateXMm) * 180.0 / Math.PI;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point mouse = e.GetPosition(this);
        if (_isPanning)
        {
            _panOffset = ClampPan(_panStartOffset + (mouse - _panStartMouse));
            InvalidateVisual();
            return;
        }

        if (!_isDragging || _design?.SelectedLayer is not { } layer)
        {
            UpdateHoverCursor(mouse);
            return;
        }

        Point mouseMm = MmFromScreen(mouse);
        switch (_dragMode)
        {
            case DragMode.Move:
                layer.TranslateXMm = _dragStartTranslateX + (mouseMm.X - _dragStartMm.X);
                layer.TranslateYMm = _dragStartTranslateY + (mouseMm.Y - _dragStartMm.Y);
                break;
            case DragMode.Scale:
            {
                double dx = mouseMm.X - layer.TranslateXMm;
                double dy = mouseMm.Y - layer.TranslateYMm;
                layer.Scale = Math.Max(MinScale, _dragStartScale * Math.Sqrt(dx * dx + dy * dy) / _dragStartDistanceMm);
                break;
            }
            case DragMode.Rotate:
            {
                double delta = AngleFromCenter(layer, mouseMm) - _dragStartMouseAngleDeg;
                while (delta <= -180) delta += 360;
                while (delta > 180) delta -= 360;
                double angle = _dragStartRotationDeg + delta;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    angle = Math.Round(angle / 90.0) * 90.0;
                    if (angle == 360) angle = 0;
                }

                layer.RotationDeg = Math.Round(angle, 1);
                break;
            }
        }
    }

    private void UpdateHoverCursor(Point mouse)
    {
        if (_design is null) return;
        bool onHandle = HitScaleHandle(mouse) is not null
            || (_design.SelectedLayer is { } selected && RotateHandlePoint(selected) is { } rotate && Near(mouse, rotate, HandleSize + 2));
        Cursor = new Cursor(onHandle ? StandardCursorType.SizeAll : StandardCursorType.Arrow);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left && _isDragging)
        {
            _isDragging = false;
            _dragMode = DragMode.None;
            e.Pointer.Capture(null);
        }
    }
}
