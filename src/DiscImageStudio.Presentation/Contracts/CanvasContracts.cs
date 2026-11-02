namespace DiscImageStudio.Controls;

// Value types exchanged between the calibration view models and the photo canvas of
// whichever UI framework hosts them.

public enum CalibrationPhotoBoundaryKind { OuterEdge, CenterHole }

public sealed record CalibrationPhotoBoundaryPoint(CalibrationPhotoBoundaryKind Boundary, int Index, double X, double Y);

public sealed record CalibrationBoundaryEdit(CalibrationPhotoBoundaryKind Boundary, int Index, double X, double Y);

public sealed record CalibrationTracePoint(int Arm, int Index, double XMm, double YMm);

/// <summary>Index -1 appends a new point; existing indices identify a point in its arm.</summary>
public sealed record CalibrationTraceEdit(int Arm, int Index, double XMm, double YMm, bool Remove = false);

/// <summary>A point in a canvas's own coordinate space (pixels or millimetres, Y down).</summary>
public readonly record struct PlanePoint(double X, double Y);

public enum StepCardState
{
    /// <summary>Not reachable yet; the body is collapsed and the chip reads 待开始.</summary>
    Pending,

    /// <summary>The step the user is on; chip reads 进行中 with the primary color.</summary>
    Active,

    /// <summary>Finished; chip reads 已完成 with the success color and the dot becomes a check.</summary>
    Done,
}
