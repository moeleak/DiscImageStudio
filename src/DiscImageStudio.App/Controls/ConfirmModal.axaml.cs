using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace DiscImageStudio.Controls;

public partial class ConfirmModal : UserControl
{
    private TaskCompletionSource<bool>? _pending;
    private TaskCompletionSource<bool?>? _pendingThreeWay;

    public ConfirmModal() => InitializeComponent();

    internal bool IsOpen => _pending is not null || _pendingThreeWay is not null;

    public Task<bool> ShowAsync(string title, string message, string confirmLabel)
    {
        EnsureIdle();
        Present(title, message, "TriangleAlert", "DangerBrush", confirmLabel, "DangerButton", neutralLabel: null);
        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }

    public Task<bool?> ShowThreeWayAsync(string title, string message, string confirmLabel, string neutralLabel)
    {
        EnsureIdle();
        Present(title, message, "Save", "TextSecondaryBrush", confirmLabel, "PrimaryButton", neutralLabel);
        _pendingThreeWay = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pendingThreeWay.Task;
    }

    /// <summary>Any key while open dismisses as "cancel" (Escape is routed here by the window).</summary>
    public bool HandleKeyDown(Key key)
    {
        if (!IsOpen) return false;
        Complete(_pendingThreeWay is not null ? null : false);
        return true;
    }

    private void EnsureIdle()
    {
        if (IsOpen) throw new InvalidOperationException("A confirmation is already pending.");
    }

    private void Present(string title, string message, string icon, string iconBrush, string confirmLabel,
        string confirmTheme, string? neutralLabel)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        HeaderIcon.Kind = icon;
        if (this.TryFindResource(iconBrush, ActualThemeVariant, out object? brush) && brush is IBrush resolved)
            HeaderIcon.Foreground = resolved;
        ConfirmButton.Content = confirmLabel;
        if (this.TryFindResource(confirmTheme, out object? theme) && theme is ControlTheme controlTheme)
            ConfirmButton.Theme = controlTheme;
        NeutralButton.Content = neutralLabel;
        NeutralButton.IsVisible = neutralLabel is not null;
        Root.IsVisible = true;
        Dispatcher.UIThread.Post(() => CancelButton.Focus(), DispatcherPriority.Input);
    }

    private void Complete(bool? confirmed)
    {
        Root.IsVisible = false;
        TaskCompletionSource<bool>? pending = _pending;
        TaskCompletionSource<bool?>? threeWay = _pendingThreeWay;
        _pending = null;
        _pendingThreeWay = null;
        pending?.TrySetResult(confirmed ?? false);
        threeWay?.TrySetResult(confirmed);
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Complete(true);

    private void Neutral_Click(object? sender, RoutedEventArgs e) => Complete(false);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Complete(null);
}
