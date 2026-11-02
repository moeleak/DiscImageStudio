using System.Collections.Concurrent;
using DiscImageStudio.Hosting;

/// <summary>A single-threaded UI loop for view-model tests on every platform (replaces the
/// WPF Dispatcher + DispatcherFrame pumping). Install it as the SynchronizationContext of the
/// test thread, then call <see cref="PumpUntil"/> to run posted continuations and timer ticks.</summary>
internal sealed class TestUiDispatcher : SynchronizationContext, IUiDispatcher
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (CheckAccess()) d(state);
        else throw new InvalidOperationException("Synchronous cross-thread Send is not supported in tests.");
    }

    public override SynchronizationContext CreateCopy() => this;

    public void Post(Action action) => Post(_ => action(), null);

    public Task InvokeAsync(Action action)
    {
        if (CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, bool background = false) => new TestTimer(this, interval, tick);

    /// <summary>Runs queued work on the calling (UI) thread until <paramref name="task"/> completes.</summary>
    public void PumpUntil(Task task, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
        _ = task.ContinueWith(_ => _queue.Add((static _ => { }, null)), TaskScheduler.Default);
        while (!task.IsCompleted)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("The UI loop did not complete the task in time.");
            if (_queue.TryTake(out var item, remaining)) item.Callback(item.State);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>Executes everything already queued without waiting for more.</summary>
    public void Drain()
    {
        while (_queue.TryTake(out var item)) item.Callback(item.State);
    }

    private sealed class TestTimer(TestUiDispatcher owner, TimeSpan interval, Action tick) : IUiTimer
    {
        private Timer? _timer;

        public TimeSpan Interval { get; set; } = interval;

        public bool IsEnabled => _timer is not null;

        public void Start()
        {
            Stop();
            _timer = new Timer(_ => owner.Post(() => { if (_timer is not null) tick(); }), null, Interval, Interval);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
