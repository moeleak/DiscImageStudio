using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Controls;

public partial class ToastHost : UserControl
{
    private static readonly Animation ExitAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(200),
        FillMode = FillMode.Forward,
        Easing = new CubicEaseIn(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d), new Setter(RenderTransformProperty, TransformOperations.Parse("translateX(0px)")) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d), new Setter(RenderTransformProperty, TransformOperations.Parse("translateX(24px)")) } },
        },
    };

    public ToastHost() => InitializeComponent();

    private void CloseToast_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ToastItem toast) toast.Close();
    }

    // The shell removes a closed toast ~220 ms later; the card leaves visibly in between.
    private void ToastCard_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border { DataContext: ToastItem toast } card)
        {
            toast.Closed += (_, _) => _ = ExitAnimation.RunAsync(card);
        }
    }
}
