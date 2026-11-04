using BTChargeIndicator.Models;

namespace BTChargeIndicator.Runtime;

/// <summary>
/// Owns the periodic scan loop and the latest immutable application state.
/// It has no dependency on a UI framework or Windows presentation APIs.
/// </summary>
internal sealed class ApplicationRuntime : IApplicationRuntime, IAsyncDisposable
{
    private readonly IBluetoothBatteryScanner _scanner;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private ApplicationRuntimeState _state = ApplicationRuntimeState.Initial;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private bool _disposed;

    public ApplicationRuntime(IBluetoothBatteryScanner scanner)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
    }

    public ApplicationRuntimeState State => Volatile.Read(ref _state);

    public event Action<ApplicationRuntimeState>? StateChanged;

    public async Task StartAsync(TimeSpan refreshInterval)
    {
        if (refreshInterval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(refreshInterval), "Refresh interval must be at least one second.");

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_loopTask is not null)
                return;

            await RefreshAsync().ConfigureAwait(false);

            _loopCancellation = new CancellationTokenSource();
            _loopTask = RunRefreshLoopAsync(refreshInterval, _loopCancellation.Token);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task RefreshAsync(bool refreshGattBattery = false)
    {
        ThrowIfDisposed();
        await _scanLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Publish(State with { IsScanning = true });

            ApplicationRuntimeState next;
            try
            {
                var result = await _scanner.ScanAsync(refreshGattBattery).ConfigureAwait(false);
                next = new ApplicationRuntimeState(
                    result.Devices.ToArray(),
                    result.Availability,
                    result.ErrorMessage,
                    DateTimeOffset.UtcNow,
                    false);
            }
            catch (Exception ex)
            {
                next = State with
                {
                    Availability = BluetoothAvailability.Error,
                    ErrorMessage = ex.Message,
                    LastUpdatedUtc = DateTimeOffset.UtcNow,
                    IsScanning = false
                };
            }

            Publish(next);
        }
        finally
        {
            if (State.IsScanning)
                Publish(State with { IsScanning = false });
            _scanLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        Task? loopTask;
        CancellationTokenSource? cancellation;
        try
        {
            loopTask = _loopTask;
            cancellation = _loopCancellation;
            _loopTask = null;
            _loopCancellation = null;
            cancellation?.Cancel();
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (loopTask is not null)
        {
            try { await loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        // Wait for any manual refresh that was started by a presentation client.
        await _scanLock.WaitAsync().ConfigureAwait(false);
        _scanLock.Release();
        cancellation?.Dispose();
    }

    private async Task RunRefreshLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await RefreshAsync().ConfigureAwait(false);
    }

    private void Publish(ApplicationRuntimeState state)
    {
        Volatile.Write(ref _state, state);
        StateChanged?.Invoke(state);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _scanLock.Dispose();
        _lifecycleLock.Dispose();
    }
}
