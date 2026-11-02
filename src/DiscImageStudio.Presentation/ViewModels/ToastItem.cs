using CommunityToolkit.Mvvm.ComponentModel;
using DiscImageStudio.Hosting;

namespace DiscImageStudio.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public partial class ToastItem : ObservableObject
{
    private readonly IUiTimer _timer;
    private bool _closed;

    [ObservableProperty]
    private bool _entered;

    public string Title { get; }

    public string Message { get; }

    public ToastKind Kind { get; }

    public TimeSpan Timeout { get; }

    public event EventHandler? Closed;

    public ToastItem(IUiDispatcher dispatcher, string title, string message, ToastKind kind, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        Title = title;
        Message = message;
        Kind = kind;
        Timeout = timeout;
        _timer = dispatcher.CreateTimer(timeout, Close, background: true);
    }

    public void BeginLifetime() => _timer.Start();

    public void Close()
    {
        _timer.Stop();
        if (_closed) return;
        _closed = true;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
