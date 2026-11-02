using Avalonia.Animation.Easings;

namespace DiscImageStudio.Controls;

/// <summary>The three motion durations and the shared ease every animation uses.</summary>
internal static class Motion
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Base = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(250);
    public static Easing EaseOut { get; } = new CubicEaseOut();
}
