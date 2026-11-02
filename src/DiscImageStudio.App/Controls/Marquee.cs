using Avalonia;
using Avalonia.Animation;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace DiscImageStudio.Controls;

/// <summary>Hover marquee for an overflowing single-line label: ease to the tail, dwell, ease
/// back, dwell, forever. Speed is clamped so short and very long overflows both read.</summary>
internal static class Marquee
{
    private const double PixelsPerSecond = 30;
    private const double MinimumSeconds = 1.8;
    private const double MaximumSeconds = 12;
    private const double DwellSeconds = 1.0;
    internal const double OverflowTolerance = 1;

    internal static CancellationTokenSource Begin(Visual target, double overflow)
    {
        double leg = Math.Clamp(overflow / PixelsPerSecond, MinimumSeconds, MaximumSeconds);
        double total = leg + leg + DwellSeconds + DwellSeconds;
        Animation animation = new()
        {
            Duration = TimeSpan.FromSeconds(total),
            IterationCount = IterationCount.Infinite,
            Easing = Motion.EaseOut,
            Children =
            {
                Frame(0, 0),
                Frame(leg / total, -overflow),
                Frame((leg + DwellSeconds) / total, -overflow),
                Frame((leg + leg + DwellSeconds) / total, 0),
                Frame(1, 0),
            },
        };
        CancellationTokenSource cancellation = new();
        _ = animation.RunAsync(target, cancellation.Token);
        return cancellation;
    }

    internal static void Stop(ref CancellationTokenSource? running)
    {
        running?.Cancel();
        running?.Dispose();
        running = null;
    }

    private static KeyFrame Frame(double cue, double x) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse(FormattableString.Invariant($"translateX({x}px)"))) },
    };
}
