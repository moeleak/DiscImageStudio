using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

/// <summary>A wrap panel whose rows are centred horizontally (start-page template tiles).</summary>
public class CenteringWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double rowWidth = 0, rowHeight = 0, widestRow = 0, height = 0;
        foreach (Control child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size desired = child.DesiredSize;
            if (rowWidth > 0 && rowWidth + desired.Width > availableSize.Width)
            {
                widestRow = Math.Max(widestRow, rowWidth);
                height += rowHeight;
                rowWidth = 0;
                rowHeight = 0;
            }

            rowWidth += desired.Width;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }

        widestRow = Math.Max(widestRow, rowWidth);
        height += rowHeight;
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? widestRow : Math.Min(widestRow, availableSize.Width),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        List<(Control Child, Size Size)> row = [];
        double rowWidth = 0, rowHeight = 0, top = 0;
        foreach (Control child in Children)
        {
            Size desired = child.DesiredSize;
            if (rowWidth > 0 && rowWidth + desired.Width > finalSize.Width)
            {
                top = ArrangeRow(row, rowWidth, rowHeight, top, finalSize.Width);
                rowWidth = 0;
                rowHeight = 0;
            }

            row.Add((child, desired));
            rowWidth += desired.Width;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }

        ArrangeRow(row, rowWidth, rowHeight, top, finalSize.Width);
        return finalSize;
    }

    private static double ArrangeRow(List<(Control Child, Size Size)> row, double rowWidth, double rowHeight,
        double top, double width)
    {
        double left = Math.Max(0, (width - rowWidth) / 2);
        foreach ((Control child, Size size) in row)
        {
            child.Arrange(new Rect(left, top, size.Width, size.Height));
            left += size.Width;
        }

        row.Clear();
        return top + rowHeight;
    }
}
