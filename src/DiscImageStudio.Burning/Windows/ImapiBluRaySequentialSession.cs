using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace DiscImageStudio.Burning;

/// <summary>
/// Unformatted BD-R SRM writing using explicit, timeout-controlled WRITE(10)
/// commands rather than formatting the medium with spare areas.
/// The recorder and speed-format COM objects remain owned by the caller.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DynamicBluRaySequentialSession : IBluRaySequentialSession
{
    private readonly dynamic _recorder;
    private readonly dynamic _speedFormat;
    private readonly BluRayMmcCommands _commands;
    private readonly IMmcCommandTransport _transport;
    private readonly Action<long, long>? _reportWriteProgress;
    private readonly Action<string>? _reportDiagnostics;
    private readonly BluRaySessionCleanup _cleanup;
    private int _cancelRequested;
    private bool _acquired;
    private bool _configured;
    private bool _disposed;

    internal DynamicBluRaySequentialSession(object recorder, object speedFormat,
        Action<long, long>? reportWriteProgress = null, Action<string>? reportCleanupWarning = null,
        Action<string>? reportDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(speedFormat);
        _recorder = recorder;
        _speedFormat = speedFormat;
        _reportWriteProgress = reportWriteProgress;
        _reportDiagnostics = reportDiagnostics;
        _transport = new ImapiMmcCommandTransport(recorder);
        _commands = new BluRayMmcCommands(_transport);
        _cleanup = new BluRaySessionCleanup(
            acquireExclusive: () => _recorder.AcquireExclusiveAccess(false, "Disc Image Studio"),
            disableMcn: () => _recorder.DisableMcn(),
            preventRemoval: () => _commands.PreventRemoval(true),
            allowRemoval: () => _commands.PreventRemoval(false),
            enableMcn: () => _recorder.EnableMcn(),
            releaseExclusive: () => _recorder.ReleaseExclusiveAccess(),
            // The direct command writer disposes its transfer buffer before
            // returning. There is no additional COM write engine to retain locks.
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
        if (requested is not null)
        {
            requested.Validate();
            _speedFormat.SetWriteSpeed(requested.SectorsPerSecond, requested.RotationTypeIsPureCav);
        }
        OpticalWriteSpeed actual = new(Convert.ToInt32(_speedFormat.CurrentWriteSpeed),
            Convert.ToBoolean(_speedFormat.CurrentRotationTypeIsPureCAV));
        actual.Validate();

        _configured = true;
        return actual;
    }

    public void Write(IStream content, uint sectorCount)
    {
        RequireAcquired();
        if (!_configured)
            throw new InvalidOperationException("必须先设置蓝光顺序写入参数。");

        // The first WRITE initializes a blank BD-R in SRM without allocating
        // spare areas. No FORMAT UNIT, RESERVE TRACK, page 05 or OPC is issued.
        BluRaySequentialWriter.Write(_transport, content, sectorCount, _reportWriteProgress,
            () => Volatile.Read(ref _cancelRequested) != 0, reportDiagnostics: _reportDiagnostics);
    }

    public void CancelWrite()
    {
        Interlocked.Exchange(ref _cancelRequested, 1);
        // Do not cancel cache draining. The caller can still flush written data
        // after cancellation; it must not close the track or disc on that path.
    }

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
        if (!_acquired) throw new InvalidOperationException("尚未独占并锁定蓝光刻录机。");
    }
}
