using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using DiscImageStudio.Controls;
using DiscImageStudio.Hosting;
using DiscImageStudio.Imaging;

internal static class CalibrationPhotoCanvasTests
{
    internal static void Run()
    {
        AvaloniaTestHost.Run(() =>
        {
            TestPhotoBoundaryEditing();
            TestRectifiedCurveEditing();
            TestPreviewAndOpenPrediction();
        });
        Console.WriteLine("calibration-photo-canvas: passed (photo pixels, corrected mm, boundary drag, single-curve editing, read-only previews)");
    }

    private static void TestPhotoBoundaryEditing()
    {
        var points = new ObservableCollection<CalibrationPhotoBoundaryPoint>();
        foreach (var boundary in new[] { CalibrationPhotoBoundaryKind.OuterEdge, CalibrationPhotoBoundaryKind.CenterHole })
        for (int index = 0; index < 8; index++)
        {
            double angle = index * Math.PI / 4;
            double radius = boundary == CalibrationPhotoBoundaryKind.OuterEdge ? 240 : 30;
            points.Add(new(boundary, index, 400 + radius * Math.Cos(angle), 300 + radius * .8 * Math.Sin(angle)));
        }
        var edits = new List<CalibrationBoundaryEdit>();
        var canvas = CreateCanvas();
        canvas.Image = Bitmap(800, 600);
        Near(new Point(450, 350), canvas.WorldToView(new Point(399.5, 299.5)),
            "source pixel centres align with the photo service's integer sampling convention");
        canvas.BoundaryPoints = points;
        canvas.BoundaryEditedCommand = new EditCommand<CalibrationBoundaryEdit>(edit =>
        {
            edits.Add(edit);
            int index = points.ToList().FindIndex(p => p.Boundary == edit.Boundary && p.Index == edit.Index);
            points[index] = new(edit.Boundary, edit.Index, edit.X, edit.Y);
        });
        var snapshot = points.ToArray();
        foreach (var zoom in new[] { 1.5, 2.0, .7 })
        {
            canvas.ZoomBy(zoom);
            canvas.PanBy(new Vector(21, -13));
            Near(new Point(714, 113), canvas.ViewToWorld(canvas.WorldToView(new Point(714, 113))), "raw photo uses invertible original-pixel coordinates");
        }
        Require(snapshot.SequenceEqual(points), "photo zoom and pan do not edit fitted boundary inputs");
        Require(!canvas.AddTraceAt(canvas.WorldToView(new Point(500, 300))), "the photo-boundary step cannot add curve observations");

        foreach (var boundary in new[] { CalibrationPhotoBoundaryKind.OuterEdge, CalibrationPhotoBoundaryKind.CenterHole })
        {
            var original = points.First(p => p.Boundary == boundary && p.Index == 1);
            var start = new Point(original.X, original.Y);
            var destination = start + new Vector(12, -8);
            Require(canvas.BeginDragAt(canvas.WorldToView(start)), "either boundary can be dragged directly without choosing its kind first");
            canvas.DragTo(canvas.WorldToView(destination));
            Require(points.Contains(original), "boundary preview does not commit intermediate samples");
            canvas.CompleteDrag();
            var updated = points.Single(p => p.Boundary == boundary && p.Index == 1);
            Near(destination, new Point(updated.X, updated.Y), "boundary edit commits original-photo pixel coordinates");
        }
        Require(edits.Count == 2 && points.Count == 16, "each boundary drag changes exactly one of the sixteen fixed controls");
        var preserved = points.ToArray();
        var probe = points[0];
        canvas.BeginDragAt(canvas.WorldToView(new Point(probe.X, probe.Y)));
        canvas.DragTo(canvas.WorldToView(new Point(probe.X + 20, probe.Y - 10)));
        canvas.CompleteDrag(false);
        canvas.RemoveSelectedTracePoint();
        Require(preserved.SequenceEqual(points) && edits.Count == 2, "cancel and Delete do not remove physical-circle boundary controls");

        canvas.BeginDragAt(canvas.WorldToView(new Point(probe.X, probe.Y)));
        canvas.DragTo(canvas.WorldToView(new Point(probe.X + 20, probe.Y - 10)));
        canvas.IsRectificationPreview = true;
        canvas.Image = Bitmap(840, 840);
        Require(edits.Count == 2, "changing to a corrected preview cancels an unfinished raw-pixel gesture");
        Require(!canvas.BeginDragAt(canvas.WorldToView(new Point(probe.X, probe.Y))) && !canvas.AddTraceAt(canvas.WorldToView(new Point(25, 0))),
            "corrected preview in step one is read-only");
        Near(new Point(-24, 45), canvas.ViewToWorld(canvas.WorldToView(new Point(-24, 45))), "corrected preview uses disc mm rather than source-image pixels");
    }

    private static void TestRectifiedCurveEditing()
    {
        var points = new ObservableCollection<CalibrationTracePoint>();
        var edits = new List<CalibrationTraceEdit>();
        var canvas = CreateCanvas();
        canvas.IsPhotoStep = false;
        canvas.Image = Bitmap(840, 840);
        canvas.TracePoints = points;
        canvas.TraceEditedCommand = new EditCommand<CalibrationTraceEdit>(edit =>
        {
            edits.Add(edit);
            var values = points.ToList();
            if (edit.Index >= 0) values.RemoveAll(p => p.Arm == edit.Arm && p.Index == edit.Index);
            if (!edit.Remove) values.Add(new(edit.Arm, edit.Index, edit.XMm, edit.YMm));
            points.Clear();
            foreach (var group in values.GroupBy(p => p.Arm))
            {
                int index = 0;
                foreach (var point in group.OrderBy(p => p.XMm * p.XMm + p.YMm * p.YMm))
                    points.Add(point with { Index = index++ });
            }
        });
        foreach (var location in new[] { new Point(18, -3), new Point(42, 9), new Point(29, 1) })
            Require(canvas.AddTraceAt(canvas.WorldToView(location)), "clicking corrected photo appends a curve sample without a fixed count");
        Require(points.Count == 3 && points.All(p => p.Arm == 0), "single curve records all samples in one curve identity");
        Require(edits.All(edit => edit.Index == -1), "new samples use the append sentinel even when the VM sorts them by radius");
        Require(!canvas.AddTraceAt(canvas.WorldToView(new Point(80, 0))), "clicks beyond the corrected image do not become observations");

        var snapshot = points.ToArray();
        canvas.ZoomBy(3);
        canvas.PanBy(new Vector(-63, 41));
        Near(new Point(29, 1), canvas.ViewToWorld(canvas.WorldToView(new Point(29, 1))), "trace zoom and pan preserve disc-centred mm");
        Require(snapshot.SequenceEqual(points), "view navigation never rewrites trace observations");
        var moved = new Point(48, 7);
        Require(canvas.BeginDragAt(canvas.WorldToView(new Point(29, 1))), "any existing curve point starts a drag on one pointer press");
        canvas.PredictionContours = [[new(-10, 0), new(0, 10), new(10, 0)]];
        canvas.DragTo(canvas.WorldToView(moved));
        Require(snapshot.SequenceEqual(points), "prediction refresh and moving preview preserve the prior observations until release");
        canvas.CompleteDrag();
        Near(moved, Position(points[^1]), "drag edits use mm and survive the VM's reordered sample indices");
        canvas.RemoveSelectedTracePoint();
        Require(points.Count == 2 && points.All(p => Point.Distance(Position(p), moved) > 1e-8), "Delete follows the moved sample after radius-based reindexing");
        Require(points.Any(p => Point.Distance(Position(p), new Point(42, 9)) < 1e-8), "Delete does not remove the point that inherited the old index");

        var prior = points.ToArray();
        canvas.BeginDragAt(canvas.WorldToView(Position(points[0])));
        canvas.DragTo(canvas.WorldToView(new Point(20, -4)));
        canvas.CompleteDrag(false);
        Require(prior.SequenceEqual(points), "Escape cancels a trace move");
        canvas.BeginDragAt(canvas.WorldToView(Position(points[0])));
        canvas.DragTo(canvas.WorldToView(new Point(20, -4)));
        canvas.IsPhotoStep = true;
        Require(prior.SequenceEqual(points), "returning to photo correction cancels an unfinished mm gesture");
        canvas.IsPhotoStep = false;
        canvas.IsTargetPreview = true;
        Require(!canvas.BeginDragAt(canvas.WorldToView(Position(points[0]))) && !canvas.AddTraceAt(canvas.WorldToView(new Point(25, 3))),
            "the engraved-target preview cannot accidentally receive photo observations");
        canvas.IsTargetPreview = false;
        canvas.Image = null;
        Require(!canvas.AddTraceAt(canvas.WorldToView(new Point(25, 3))), "a missing photo never accepts guessed curve observations");
    }

    private static void TestPreviewAndOpenPrediction()
    {
        var canvas = CreateCanvas();
        canvas.IsPhotoStep = false;
        canvas.Image = Bitmap(840, 840);
        canvas.PredictionContours = [[new(-30, 0), new(-10, -20), new(10, 0)]];
        using var bitmap = Render(canvas);
        Require(HasPredictionNear(bitmap, canvas.WorldToView(new Point(-20, -10))), "full predicted curve is drawn over the corrected photo");
        Require(!HasPredictionNear(bitmap, canvas.WorldToView(new Point(-10, 0))), "predicted curve is open and never gains an invented closing segment");
        canvas.ShowPrediction = false;
        using var hidden = Render(canvas);
        Require(!HasPredictionNear(hidden, canvas.WorldToView(new Point(-20, -10))), "prediction overlay can be hidden to inspect the real photo");
        canvas.TracePoints = [new(0, 0, -30, 0), new(0, 2, -10, -20), new(0, 1, 10, 0)];
        using var inserted = Render(canvas);
        Require(HasColorNear(inserted, canvas.WorldToView(new Point(-20, -10)), cyan: true),
            "inserted samples connect in the supplied curve order, independent of stable editing IDs");
        Require(!HasColorNear(inserted, canvas.WorldToView(new Point(-10, 0)), cyan: true),
            "stable IDs do not reconnect an inserted sample in its original creation order");
        canvas.Image = null;
        Render(canvas).Dispose();
        Require(!canvas.BeginDragAt(new Point(450, 350)), "the no-photo concentric-cross placeholder is a read-only preview");
    }

    private static CalibrationPhotoCanvas CreateCanvas()
    {
        var canvas = new CalibrationPhotoCanvas { CanvasRadiusMm = 63 };
        canvas.Measure(new Size(900, 700));
        canvas.Arrange(new Rect(0, 0, 900, 700));
        return canvas;
    }
    private static Bitmap Bitmap(int width, int height)
        => AvaloniaBitmaps.ToBitmap(new Gray8Image(width, height, new byte[width * height]))!;
    private static RenderTargetBitmap Render(CalibrationPhotoCanvas canvas)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(900, 700));
        bitmap.Render(canvas);
        return bitmap;
    }
    private static Point Position(CalibrationTracePoint point) => new(point.XMm, point.YMm);
    private static bool HasPredictionNear(Bitmap bitmap, Point point) => HasColorNear(bitmap, point, cyan: false);
    private static bool HasColorNear(Bitmap bitmap, Point point, bool cyan)
    {
        int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (x + dx < 0 || y + dy < 0 || x + dx >= bitmap.PixelSize.Width || y + dy >= bitmap.PixelSize.Height) continue;
            var pixel = new byte[4];
            GCHandle handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(x + dx, y + dy, 1, 1), handle.AddrOfPinnedObject(), 4, 4); }
            finally { handle.Free(); }
            // Skia renders BGRA on Windows/Linux and RGBA on macOS; compare in B, G, R order.
            bool rgba = bitmap.Format == Avalonia.Platform.PixelFormats.Rgba8888;
            byte blue = rgba ? pixel[2] : pixel[0], green = pixel[1], red = rgba ? pixel[0] : pixel[2];
            if (cyan ? blue > 180 && green > 180 && red < 100 : blue < 90 && green > 80 && green < 210 && red > 180) return true;
        }
        return false;
    }
    private static void Near(Point expected, Point actual, string message)
        => Require(Point.Distance(expected, actual) < 1e-8, $"{message} (expected {expected}, actual {actual})");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Calibration photo canvas: {message}");
    }
    private sealed class EditCommand<T>(Action<T> edit) : ICommand
    {
        public bool CanExecute(object? parameter) => parameter is T;
        public void Execute(object? parameter) => edit((T)parameter!);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
