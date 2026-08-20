namespace Shigure;

/// <summary>
/// 串行管理运行时会话，确保并发的启动、重启和停止请求不会清理到错误的实例。
/// </summary>
internal sealed class RuntimeSessionCoordinator : IAsyncDisposable
{
    private readonly IShigureRuntimeFactory _runtimeFactory;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _requestSync = new();
    private RuntimeSession? _current;
    private long _nextSessionId;
    private long _stoppedSessionId;
    private long _latestRequestVersion;
    private bool _disposed;

    public RuntimeSessionCoordinator(IShigureRuntimeFactory runtimeFactory)
    {
        _runtimeFactory = runtimeFactory;
    }

    public event Action<long, RenderSnapshot>? SnapshotUpdated;

    public event Action<long, Exception>? RuntimeFailed;

    public event Action<long>? RuntimeStopped;

    public bool HasSession => Volatile.Read(ref _current) is not null;

    public bool IsRunning
    {
        get
        {
            var current = Volatile.Read(ref _current);
            return current is { RunTask.IsCompleted: false }
                && Volatile.Read(ref _stoppedSessionId) != current.Id;
        }
    }

    public AppOptions? CurrentOptions => Volatile.Read(ref _current)?.Options;

    public long? CurrentSessionId => Volatile.Read(ref _current)?.Id;

    public Task StartAsync(
        AppOptions options,
        long requestVersion,
        CancellationToken cancellationToken = default)
        => ChangeSessionAsync(options, requestVersion, restart: false, cancellationToken);

    public Task RestartAsync(
        AppOptions options,
        long requestVersion,
        CancellationToken cancellationToken = default)
        => ChangeSessionAsync(options, requestVersion, restart: true, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCurrentCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void ToggleEnabled()
    {
        var session = Volatile.Read(ref _current);
        if (session is { RunTask.IsCompleted: false })
        {
            session.Runtime.ToggleEnabled();
        }
    }

    public void SetEnabled(bool enabled)
    {
        var session = Volatile.Read(ref _current);
        if (session is { RunTask.IsCompleted: false })
        {
            session.Runtime.SetEnabled(enabled);
        }
    }

    public void ActivateBurst()
    {
        var session = Volatile.Read(ref _current);
        if (session is { RunTask.IsCompleted: false })
        {
            session.Runtime.ActivateBurst();
        }
    }

    public void ToggleBurst()
    {
        var session = Volatile.Read(ref _current);
        if (session is { RunTask.IsCompleted: false })
        {
            session.Runtime.ToggleBurst();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await StopCurrentCoreAsync().ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ChangeSessionAsync(
        AppOptions options,
        long requestVersion,
        bool restart,
        CancellationToken cancellationToken)
    {
        RegisterLatestRequest(requestVersion);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (requestVersion != Volatile.Read(ref _latestRequestVersion))
            {
                return;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            var current = _current;
            if (!restart
                && current is not null
                && IsRunning
                && current.Options == options)
            {
                return;
            }

            if (current is not null)
            {
                await StopCurrentCoreAsync().ConfigureAwait(false);
            }

            if (requestVersion != Volatile.Read(ref _latestRequestVersion))
            {
                return;
            }

            StartCore(options, requestVersion);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void RegisterLatestRequest(long requestVersion)
    {
        lock (_requestSync)
        {
            if (requestVersion > _latestRequestVersion)
            {
                Volatile.Write(ref _latestRequestVersion, requestVersion);
            }
        }
    }

    private void StartCore(AppOptions options, long requestVersion)
    {
        var runtime = _runtimeFactory.Create(options);
        var cancellation = new CancellationTokenSource();
        var sessionId = Interlocked.Increment(ref _nextSessionId);
        Action<RenderSnapshot> snapshotHandler = snapshot => SnapshotUpdated?.Invoke(sessionId, snapshot);

        lock (_requestSync)
        {
            if (requestVersion != _latestRequestVersion)
            {
                cancellation.Dispose();
                return;
            }

            runtime.SnapshotUpdated += snapshotHandler;
            try
            {
                var runTask = Task.Run(() => RunRuntimeAsync(sessionId, runtime, cancellation.Token));
                Volatile.Write(
                    ref _current,
                    new RuntimeSession(sessionId, options, runtime, cancellation, runTask, snapshotHandler));
            }
            catch
            {
                runtime.SnapshotUpdated -= snapshotHandler;
                cancellation.Dispose();
                throw;
            }
        }
    }

    private async Task StopCurrentCoreAsync()
    {
        var session = _current;
        if (session is null)
        {
            return;
        }

        session.Cancellation.Cancel();
        try
        {
            await session.RunTask.ConfigureAwait(false);
        }
        finally
        {
            session.Runtime.SnapshotUpdated -= session.SnapshotHandler;
            session.Cancellation.Dispose();
            if (ReferenceEquals(Volatile.Read(ref _current), session))
            {
                Volatile.Write(ref _current, null);
            }
        }
    }

    private async Task RunRuntimeAsync(
        long sessionId,
        ShigureRuntime runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停止路径。
        }
        catch (Exception ex)
        {
            RuntimeFailed?.Invoke(sessionId, ex);
        }
        finally
        {
            Volatile.Write(ref _stoppedSessionId, sessionId);
            RuntimeStopped?.Invoke(sessionId);
        }
    }

    private sealed record RuntimeSession(
        long Id,
        AppOptions Options,
        ShigureRuntime Runtime,
        CancellationTokenSource Cancellation,
        Task RunTask,
        Action<RenderSnapshot> SnapshotHandler);
}
