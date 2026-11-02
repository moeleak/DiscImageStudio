using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace DiscImageStudio.Controls;

/// <summary>Disc drawing shared by the code-drawn canvases (body, hub, content ring, guides).</summary>
internal static class DiscRenderCore
{
    public static void DrawDiscBody(DrawingContext context, Point center, double discRadius, double hubRadius)
    {
        context.DrawEllipse(DiscTheme.DiscBody, null, center, discRadius, discRadius);
        context.DrawEllipse(DiscTheme.Hub, null, center, hubRadius, hubRadius);
    }

    public static void DrawContentRing(DrawingContext context, Point center, double innerRadius, double outerRadius)
    {
        if (outerRadius <= innerRadius) return;
        GeometryGroup ring = new() { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new EllipseGeometry(new Rect(center.X - outerRadius, center.Y - outerRadius, outerRadius * 2, outerRadius * 2)));
        ring.Children.Add(new EllipseGeometry(new Rect(center.X - innerRadius, center.Y - innerRadius, innerRadius * 2, innerRadius * 2)));
        context.DrawGeometry(DiscTheme.Ring, null, ring);
    }

    public static void DrawGuideCircles(DrawingContext context, Point center, double discRadius, double outerRadius,
        double innerRadius)
    {
        Pen pen = new(DiscTheme.Guide, 1);
        context.DrawEllipse(null, pen, center, discRadius, discRadius);
        context.DrawEllipse(null, pen, center, outerRadius, outerRadius);
        if (innerRadius > 0) context.DrawEllipse(null, pen, center, innerRadius, innerRadius);
    }

    public static void DrawOriginCross(DrawingContext context, Point center, double arm)
    {
        Pen pen = new(DiscTheme.Guide, 1);
        context.DrawLine(pen, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
        context.DrawLine(pen, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
    }

    public static FormattedText CreateText(string text, double size, IBrush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, DiscTheme.Typeface, size, brush);

    public static PathFigure CircleFigure(Point center, double radius)
    {
        PathFigure figure = new() { StartPoint = new Point(center.X - radius, center.Y), IsClosed = true, Segments = [] };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(center.X + radius, center.Y), Size = new Size(radius, radius),
            IsLargeArc = true, SweepDirection = SweepDirection.Clockwise,
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(center.X - radius, center.Y), Size = new Size(radius, radius),
            IsLargeArc = true, SweepDirection = SweepDirection.Clockwise,
        });
        return figure;
    }

    public static PathFigure RectangleFigure(Rect rect)
    {
        PathFigure figure = new() { StartPoint = rect.TopLeft, IsClosed = true, Segments = [] };
        figure.Segments.Add(new LineSegment { Point = rect.TopRight });
        figure.Segments.Add(new LineSegment { Point = rect.BottomRight });
        figure.Segments.Add(new LineSegment { Point = rect.BottomLeft });
        return figure;
    }
}
