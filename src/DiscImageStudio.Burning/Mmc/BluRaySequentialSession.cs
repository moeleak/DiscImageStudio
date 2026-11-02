using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

internal sealed record BluRaySequentialMediaInfo(
    int Profile,
    bool DiscIsBlank,
    int FormatStatus,
    bool TrackIsBlank,
    bool NextWritableAddressValid,
    uint TrackStartAddress,
    uint NextWritableAddress,
    uint FreeBlocks,
    uint TrackSize,
    uint MaximumBlocks,
    int TrackNumber);

internal interface IBluRaySequentialSession : IDisposable
{
    void Acquire();
    BluRaySequentialMediaInfo ReadMedia();
    OpticalWriteSpeed ConfigureWrite(OpticalWriteSpeed? requested);
    void Write(IStream content, uint sectorCount);
    void CancelWrite();
    void Flush();
    void CloseTrack(int trackNumber);
    void CloseDisc();
    void Release();
}

/// <summary>
/// Tracks only this session's confirmed IMAPI acquisitions and its uncertain MMC
/// removal lock. Failed unlocks remain pending. Disposal releases the write engine
/// before one final retry, without repeating successful, reference-counted releases.
/// Delegates keep this lifecycle testable without COM or a physical recorder.
/// </summary>
internal sealed class BluRaySessionCleanup : IDisposable
{
    private readonly Action _acquireExclusive;
    private readonly Action _disableMcn;
    private readonly Action _preventRemoval;
    private readonly Action _allowRemoval;
    private readonly Action _enableMcn;
    private readonly Action _releaseExclusive;
    private readonly Action _releaseWriteEngine;
    private readonly Action<string>? _reportCleanupWarning;
    private bool _exclusive;
    private bool _mcnDisabled;
    private bool _removalPrevented;
    private bool _releaseAttempted;
    private bool _disposed;

    internal BluRaySessionCleanup(Action acquireExclusive, Action disableMcn, Action preventRemoval,
        Action allowRemoval, Action enableMcn, Action releaseExclusive, Action releaseWriteEngine,
        Action<string>? reportCleanupWarning = null)
    {
        _acquireExclusive = acquireExclusive ?? throw new ArgumentNullException(nameof(acquireExclusive));
        _disableMcn = disableMcn ?? throw new ArgumentNullException(nameof(disableMcn));
        _preventRemoval = preventRemoval ?? throw new ArgumentNullException(nameof(preventRemoval));
        _allowRemoval = allowRemoval ?? throw new ArgumentNullException(nameof(allowRemoval));
        _enableMcn = enableMcn ?? throw new ArgumentNullException(nameof(enableMcn));
        _releaseExclusive = releaseExclusive ?? throw new ArgumentNullException(nameof(releaseExclusive));
        _releaseWriteEngine = releaseWriteEngine ?? throw new ArgumentNullException(nameof(releaseWriteEngine));
        _reportCleanupWarning = reportCleanupWarning;
    }

    internal bool HasPendingLocks => _exclusive || _mcnDisabled || _removalPrevented;

    internal void Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HasPendingLocks) throw new InvalidOperationException("蓝光会话仍有未释放的刻录机访问权。");
        _releaseAttempted = false;
        try
        {
            // Exclusive access is never forced. Its release and MCN's paired enable
            // are owed only after the corresponding acquisition reports success.
            _acquireExclusive();
            _exclusive = true;
            _disableMcn();
            _mcnDisabled = true;
            // A failed PREVENT transport can still have locked the device.
            _removalPrevented = true;
            _preventRemoval();
        }
        catch
        {
            try { Release(); } catch { /* Keep the acquisition exception and pending flags. */ }
            throw;
        }
    }

    internal void Release()
    {
        if (_disposed) return;
        ReleaseCore(retry: false);
    }

    private void ReleaseCore(bool retry)
    {
        _releaseAttempted = true;
        Exception? first = null;
        ReleaseOne(ref _removalPrevented, _allowRemoval, "解除盘片弹出保护", retry, ref first);
        ReleaseOne(ref _mcnDisabled, _enableMcn, "恢复介质变化通知", retry, ref first);
        ReleaseOne(ref _exclusive, _releaseExclusive, "释放刻录机独占访问", retry, ref first);
        if (first is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }

    private void ReleaseOne(ref bool pending, Action release, string operation, bool retry, ref Exception? first)
    {
        if (!pending) return;
        try
        {
            release();
            pending = false;
        }
        catch (Exception exception)
        {
            first ??= exception;
            ReportFailure(operation, exception, retry);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Release may already have run in the burn or failed-acquisition path.
        // Do not waste its only disposal retry while the engine still owns COM references.
        if (!_releaseAttempted)
        {
            try { ReleaseCore(retry: false); } catch { }
        }
        try { _releaseWriteEngine(); }
        catch (Exception exception) { ReportFailure("释放蓝光写入引擎", exception, retry: false); }
        if (HasPendingLocks)
        {
            try { ReleaseCore(retry: true); } catch { }
        }
    }

    private void ReportFailure(string operation, Exception exception, bool retry)
    {
        try
        {
            _reportCleanupWarning?.Invoke(
                $"蓝光清理{(retry ? "重试" : string.Empty)}：{operation}失败（HRESULT 0x{exception.HResult:X8}）：{exception.Message}");
        }
        catch { /* Diagnostics must never interrupt remaining unlocks or replace a write failure. */ }
    }
}
