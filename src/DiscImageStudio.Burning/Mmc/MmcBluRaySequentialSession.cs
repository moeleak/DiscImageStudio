using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

/// <summary>
/// Unformatted BD-R SRM over a raw MMC transport (Linux SG_IO, macOS SCSITask): the same
/// commands and WRITE stream as the Windows session, with exclusive access provided by the
/// already-open device handle and the write speed set through SET STREAMING.
/// </summary>
internal sealed class MmcBluRaySequentialSession : IBluRaySequentialSession
{
    private readonly IMmcCommandTransport _transport;
    private readonly BluRayMmcCommands _commands;
    private readonly MmcCommandSet _general;
    private readonly Action<long, long>? _reportWriteProgress;
    private readonly Action<string>? _reportDiagnostics;
    private readonly BluRaySessionCleanup _cleanup;
    private int _cancelRequested;
    private bool _acquired;
    private bool _configured;
    private bool _disposed;

    internal MmcBluRaySequentialSession(IMmcCommandTransport transport, Action<long, long>? reportWriteProgress = null,
        Action<string>? reportCleanupWarning = null, Action<string>? reportDiagnostics = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _commands = new BluRayMmcCommands(transport);
        _general = new MmcCommandSet(transport);
        _reportWriteProgress = reportWriteProgress;
        _reportDiagnostics = reportDiagnostics;
        _cleanup = new BluRaySessionCleanup(
            acquireExclusive: () => { },  // the device handle is exclusive (O_EXCL / ObtainExclusiveAccess)
            disableMcn: () => { },
            preventRemoval: () => _commands.PreventRemoval(true),
            allowRemoval: () => _commands.PreventRemoval(false),
            enableMcn: () => { },
            releaseExclusive: () => { },
            releaseWriteEngine: () => { },
            reportCleanupWarning: reportCleanupWarning);
    }

    public void Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_acquired || _cleanup.HasPendingLocks)
            throw new InvalidOperationException("蓝光会话已经取得刻录机访问权。");
        _cleanup.Acquire();
        _acquired = true;
    }

    public BluRaySequentialMediaInfo ReadMedia()
    {
        RequireAcquired();
        return _commands.ReadMedia();
    }

    public OpticalWriteSpeed ConfigureWrite(OpticalWriteSpeed? requested)
    {
        RequireAcquired();
        if (_configured) throw new InvalidOperationException("蓝光写入参数已经设置。");
        OpticalWriteSpeed speed = requested is null
            ? MmcSpeedControl.DriveDefault(_general, OpticalBurnMediaKind.BluRayData)
            : MmcSpeedControl.Apply(_general, OpticalBurnMediaKind.BluRayData, requested,
                OpticalBurnWorkflows.BluRaySingleLayerSectors - 1, _reportDiagnostics);
        _configured = true;
        return speed;
    }

    public void Write(IStream content, uint sectorCount)
    {
        RequireAcquired();
        if (!_configured) throw new InvalidOperationException("必须先设置蓝光顺序写入参数。");
        // As on Windows: the first WRITE initializes the blank BD-R in SRM without spare
        // areas. No FORMAT UNIT, RESERVE TRACK, page 05h or OPC is issued.
        BluRaySequentialWriter.Write(_transport, content, sectorCount, _reportWriteProgress,
            () => Volatile.Read(ref _cancelRequested) != 0, reportDiagnostics: _reportDiagnostics);
    }

    public void CancelWrite() => Interlocked.Exchange(ref _cancelRequested, 1);

    public void Flush()
    {
        RequireAcquired();
        _commands.SynchronizeCache();
    }

    public void CloseTrack(int trackNumber)
    {
        RequireAcquired();
        _commands.CloseTrack(trackNumber);
    }

    public void CloseDisc()
    {
        RequireAcquired();
        _commands.CloseDisc();
    }

    public void Release()
    {
        _acquired = false;
        _configured = false;
        _cleanup.Release();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _acquired = false;
        _configured = false;
        _cleanup.Dispose();
    }

    private void RequireAcquired()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_acquired) throw new InvalidOperationException("尚未锁定蓝光刻录机。");
    }
}
