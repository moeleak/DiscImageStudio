using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace DiscImageStudio.Hosting;

/// <summary>Lets keyframe animations target <c>RenderTransform</c> with CSS-style transform
/// values (<c>translateX(24px)</c>). Avalonia 12 only animates the properties of individual
/// transform objects in keyframes; this interpolates whole transforms the way
/// <see cref="TransformOperationsTransition"/> does.</summary>
internal sealed class TransformKeyFrameAnimator : InterpolatingAnimator<ITransform>
{
    private static int s_registered;

    internal static void Register()
    {
        if (Interlocked.Exchange(ref s_registered, 1) == 0)
        {
            Animation.RegisterCustomAnimator<ITransform, TransformKeyFrameAnimator>();
        }
    }

    public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue)
        => TransformOperations.Interpolate(ToOperations(oldValue), ToOperations(newValue), progress);

    private static TransformOperations ToOperations(ITransform? transform) => transform switch
    {
        TransformOperations operations => operations,
        null => TransformOperations.Identity,
        _ => BuildMatrix(transform),
    };

    private static TransformOperations BuildMatrix(ITransform transform)
    {
        TransformOperations.Builder builder = TransformOperations.CreateBuilder(1);
        builder.AppendMatrix(transform.Value);
        return builder.Build();
    }
}
