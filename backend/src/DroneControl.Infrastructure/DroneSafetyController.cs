using System.Diagnostics;
using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Fail-closed controller decorator that bounds movement/liveness, serializes
/// STOP and movement requests, and prevents stale watchdogs from changing the
/// current software safety state. Every STOP travels through the same inner
/// IDroneController path as ordinary commands. A completed software STOP is
/// never a claim of physical motor stop.
/// </summary>
public sealed class DroneSafetyController : IDroneController
{
    private readonly IDroneController _inner;
    private readonly DroneSafetyOptions _options;
    private readonly Action<DroneSafetyEvent> _emit;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private DroneSafetyState _state = DroneSafetyState.Unverified;
    private bool _sessionConnected;
    private DroneCommand _requestedCommand = DroneCommand.Stop;
    private DroneCommand _confirmedCommand = DroneCommand.Stop;
    private int _speedPercent;
    private long _generation;
    private CancellationTokenSource? _movementLease;
    private Task? _watchdogTask;

    public DroneSafetyController(
        IDroneController inner,
        DroneSafetyOptions options,
        Action<DroneSafetyEvent> emit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
    }

    public DroneSafetyState CurrentSafetyState
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Establishes a software safe baseline before requests are served. On
    /// failure the host can remain available for health/status, but this
    /// controller stays Faulted and rejects movement.
    /// </summary>
    public async Task StartSafetyAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (RealHardwareSafetyIsUnverified)
            {
                SetState(DroneSafetyState.Faulted, DroneSafetyReason.Startup,
                    "HARDWARE_MODE=real is disabled until an external abrupt-failure safety mechanism is verified.");
                return;
            }

            SetState(DroneSafetyState.Recovering, DroneSafetyReason.Startup,
                "Establishing startup software safe baseline.");
            Exception? lastFailure = null;

            for (var attempt = 0; attempt <= _options.RecoveryRetryCount; attempt++)
            {
                try
                {
                    await EstablishBaselineAsync(disconnectAfterStop: true, cancellationToken).ConfigureAwait(false);
                    _sessionConnected = false;
                    SetState(DroneSafetyState.Safe, DroneSafetyReason.Startup,
                        "Startup STOP request completed at software level; physical safety remains unverified.");
                    return;
                }
                catch (Exception exception) when (IsOperationalFailure(exception))
                {
                    lastFailure = exception;
                    await BestEffortDisconnectAsync().ConfigureAwait(false);
                    if (attempt < _options.RecoveryRetryCount)
                    {
                        await DelayRetryAsync(_options.RecoveryRetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            SetState(DroneSafetyState.Faulted,
                lastFailure is DroneUnavailableException ? DroneSafetyReason.HardwareUnavailable : DroneSafetyReason.RecoveryFailure,
                "Startup safe baseline could not be established; movement is unavailable.", lastFailure);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Attempts semantic STOP during graceful host shutdown with bounded retries.</summary>
    public async Task StopSafetyAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (RealHardwareSafetyIsUnverified)
            {
                SetState(DroneSafetyState.Faulted, DroneSafetyReason.GracefulShutdown,
                    "STOP was not attempted: real hardware is disabled pending external safety verification.");
                return;
            }

            CancelMovementLease();
            SetState(DroneSafetyState.StopPending, DroneSafetyReason.GracefulShutdown,
                "Graceful shutdown requested semantic STOP.");

            try
            {
                if (!_sessionConnected)
                {
                    await ExecuteBoundedAsync(
                        token => _inner.ConnectAsync(token), _options.CommandTimeout, cancellationToken)
                        .ConfigureAwait(false);
                    _sessionConnected = true;
                }

                await RequestStopWithRetriesAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteBoundedAsync(
                    token => _inner.DisconnectAsync(token), _options.StopTimeout, cancellationToken)
                    .ConfigureAwait(false);
                _sessionConnected = false;
                ResetCommandSnapshot();
                SetState(DroneSafetyState.Safe, DroneSafetyReason.GracefulShutdown,
                    "Shutdown STOP request completed at software level; physical stop is unverified.");
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                await BestEffortDisconnectAsync().ConfigureAwait(false);
                SetState(DroneSafetyState.Faulted,
                    exception is DroneUnavailableException ? DroneSafetyReason.HardwareUnavailable : DroneSafetyReason.HardwareException,
                    "Graceful shutdown STOP failed or was unavailable.", exception);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _inner.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (CurrentSafetyState is DroneSafetyState.Unverified or DroneSafetyState.StopPending
            or DroneSafetyState.Faulted or DroneSafetyState.Recovering)
        {
            // Keep the existing wire shape but do not expose an apparently
            // healthy connection while the software safety path is closed.
            return status with
            {
                State = status.State with
                {
                    Connection = ConnectionStatus.Error,
                    Camera = CameraStatus.Offline,
                    RequestedCommand = _requestedCommand,
                    ConfirmedCommand = _confirmedCommand,
                    Speed = _speedPercent,
                },
            };
        }

        lock (_stateGate)
        {
            return status with
            {
                State = status.State with
                {
                    RequestedCommand = _requestedCommand,
                    ConfirmedCommand = _confirmedCommand,
                    Speed = _speedPercent,
                },
            };
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (RealHardwareSafetyIsUnverified)
            {
                SetState(DroneSafetyState.Faulted, DroneSafetyReason.RecoveryFailure,
                    "HARDWARE_MODE=real is disabled until external abrupt-failure safety is verified.");
                throw new DroneUnavailableException();
            }

            CancelMovementLease();
            SetState(DroneSafetyState.Recovering, DroneSafetyReason.ConnectionLoss,
                "Re-establishing a safe baseline before enabling movement.");
            Exception? lastFailure = null;

            for (var attempt = 0; attempt <= _options.RecoveryRetryCount; attempt++)
            {
                try
                {
                    await EstablishBaselineAsync(disconnectAfterStop: false, cancellationToken).ConfigureAwait(false);
                    _sessionConnected = true;
                    SetState(DroneSafetyState.Safe, DroneSafetyReason.Startup,
                        "Connection baseline completed; physical safety remains unverified.");
                    return;
                }
                catch (Exception exception) when (IsOperationalFailure(exception))
                {
                    lastFailure = exception;
                    await BestEffortDisconnectAsync().ConfigureAwait(false);
                    if (attempt < _options.RecoveryRetryCount)
                    {
                        await DelayRetryAsync(_options.RecoveryRetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            SetState(DroneSafetyState.Faulted,
                lastFailure is DroneUnavailableException ? DroneSafetyReason.HardwareUnavailable : DroneSafetyReason.RecoveryFailure,
                "Connection recovery could not establish a safe baseline.", lastFailure);
            throw lastFailure ?? new DroneUnavailableException();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelMovementLease();
            Exception? failure = null;
            var wasFaulted = CurrentSafetyState == DroneSafetyState.Faulted;

            if (_sessionConnected)
            {
                SetState(DroneSafetyState.StopPending, DroneSafetyReason.ConnectionLoss,
                    "Disconnect requested; stopping through the supervised controller path.");
                try
                {
                    await RequestStopWithRetriesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsOperationalFailure(exception))
                {
                    failure = exception;
                }
            }

            try
            {
                await ExecuteBoundedAsync(
                    token => _inner.DisconnectAsync(token), _options.StopTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                failure ??= exception;
            }
            finally
            {
                _sessionConnected = false;
                ResetCommandSnapshot();
            }

            if (failure is not null)
            {
                SetState(DroneSafetyState.Faulted,
                    failure is DroneUnavailableException ? DroneSafetyReason.HardwareUnavailable : DroneSafetyReason.HardwareException,
                    "Disconnect STOP/reset failed; movement remains blocked.", failure);
                throw failure;
            }

            SetState(wasFaulted && !_sessionConnected ? DroneSafetyState.Faulted : DroneSafetyState.Safe,
                DroneSafetyReason.ConnectionLoss,
                wasFaulted && !_sessionConnected
                    ? "Disconnected, but the prior fault remains until an explicit safe-baseline recovery succeeds."
                    : "Disconnected at software level; physical stop is not claimed.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown drone command.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (command != DroneCommand.Stop
                && CurrentSafetyState is not (DroneSafetyState.Safe or DroneSafetyState.CommandActive))
            {
                throw new DroneUnavailableException();
            }

            CancelMovementLease();
            var generation = Interlocked.Increment(ref _generation);

            if (command == DroneCommand.Stop)
            {
                var priorSafetyState = CurrentSafetyState;
                if (!_sessionConnected
                    && priorSafetyState is (DroneSafetyState.Faulted or DroneSafetyState.Recovering or DroneSafetyState.Unverified))
                {
                    // No session is available for a STOP attempt. Do not turn a
                    // prior safety fault into a healthy state merely because a
                    // disconnected controller has no work to do.
                    throw new DroneUnavailableException();
                }

                lock (_stateGate)
                {
                    _requestedCommand = DroneCommand.Stop;
                }
                SetState(DroneSafetyState.StopPending, DroneSafetyReason.StopRequested,
                    "Semantic STOP requested through the supervised controller path.");
                try
                {
                    if (_sessionConnected)
                    {
                        await RequestStopWithRetriesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Preserve the existing offline command response (409)
                        // without claiming a hardware operation occurred.
                        await _inner.SendCommandAsync(DroneCommand.Stop, cancellationToken).ConfigureAwait(false);
                    }
                    lock (_stateGate)
                    {
                        _confirmedCommand = DroneCommand.Stop;
                    }
                    SetState(DroneSafetyState.Safe, DroneSafetyReason.StopRequested,
                        "STOP request completed at software level; physical stop is unverified.");
                }
                catch (DroneNotConnectedException)
                {
                    // The frozen API behavior for a command while offline is
                    // 409. No movement session exists to stop. Preserve an
                    // existing fault; otherwise this is a safe offline no-op.
                    var nextState = priorSafetyState == DroneSafetyState.Faulted
                        ? DroneSafetyState.Faulted
                        : DroneSafetyState.Safe;
                    SetState(nextState, DroneSafetyReason.InvalidOrRejectedOperation,
                        "STOP requested while disconnected; no active session was reported.");
                    throw;
                }
                catch (Exception exception) when (IsOperationalFailure(exception))
                {
                    SetState(DroneSafetyState.Faulted, DroneSafetyReason.HardwareException,
                        "Semantic STOP failed; movement is blocked.", exception);
                    throw;
                }

                return;
            }

            SetState(DroneSafetyState.CommandActive, DroneSafetyReason.MovementCommand,
                "Movement command is active within its configured liveness window.");
            lock (_stateGate)
            {
                _requestedCommand = command;
            }
            var commandStartedAt = Stopwatch.GetTimestamp();
            try
            {
                await ExecuteBoundedAsync(
                    token => _inner.SendCommandAsync(command, token), _options.CommandTimeout, cancellationToken)
                    .ConfigureAwait(false);

                var remainingLiveness = _options.CommandTimeout - Stopwatch.GetElapsedTime(commandStartedAt);
                if (remainingLiveness <= TimeSpan.Zero)
                {
                    throw new TimeoutException("The command consumed its complete liveness deadline.");
                }

                lock (_stateGate)
                {
                    _confirmedCommand = command;
                }

                var lease = new CancellationTokenSource();
                _movementLease = lease;
                _watchdogTask = WatchMovementAsync(generation, lease.Token, remainingLiveness);
            }
            catch (DroneNotConnectedException) when (!_sessionConnected)
            {
                // Preserve the existing 409 behavior for a request made with
                // no session. There is no active movement to stop or fault.
                SetState(DroneSafetyState.Safe, DroneSafetyReason.ConnectionLoss,
                    "Movement was rejected because no controller session is connected.");
                throw;
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                CancelMovementLease();
                var reason = exception switch
                {
                    TimeoutException => DroneSafetyReason.CommandTimeout,
                    DroneUnavailableException => DroneSafetyReason.HardwareUnavailable,
                    _ => DroneSafetyReason.HardwareException,
                };
                SetState(DroneSafetyState.StopPending, reason,
                    "Movement command failed or timed out; requesting semantic STOP.", exception);
                var stopped = await TryStopAfterFailureAsync(CancellationToken.None).ConfigureAwait(false);
                SetState(stopped ? DroneSafetyState.Safe : DroneSafetyState.Faulted, reason,
                    stopped
                        ? "STOP request completed at software level after movement failure; physical stop is unverified."
                        : "STOP attempt failed after movement failure; movement remains blocked.",
                    stopped ? null : exception);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
    {
        if (speed is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Speed must be between 0 and 100.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (CurrentSafetyState is not (DroneSafetyState.Safe or DroneSafetyState.CommandActive))
            {
                throw new DroneUnavailableException();
            }

            try
            {
                await ExecuteBoundedAsync(
                    token => _inner.SetSpeedAsync(speed, token), _options.CommandTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (_sessionConnected)
                {
                    lock (_stateGate)
                    {
                        _speedPercent = speed;
                    }
                }
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                CancelMovementLease();
                var reason = exception switch
                {
                    TimeoutException => DroneSafetyReason.CommandTimeout,
                    DroneUnavailableException => DroneSafetyReason.HardwareUnavailable,
                    _ => DroneSafetyReason.HardwareException,
                };
                SetState(DroneSafetyState.StopPending, reason,
                    "Speed operation failed or timed out; requesting semantic STOP.", exception);
                var stopped = await TryStopAfterFailureAsync(CancellationToken.None).ConfigureAwait(false);
                SetState(stopped ? DroneSafetyState.Safe : DroneSafetyState.Faulted, reason,
                    stopped ? "STOP request completed after speed failure." : "STOP failed after speed failure.",
                    stopped ? null : exception);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task EstablishBaselineAsync(bool disconnectAfterStop, CancellationToken cancellationToken)
    {
        await ExecuteBoundedAsync(
            token => _inner.ConnectAsync(token), _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
        _sessionConnected = true;
        await RequestStopWithRetriesAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            _requestedCommand = DroneCommand.Stop;
            _confirmedCommand = DroneCommand.Stop;
        }
        if (disconnectAfterStop)
        {
            await ExecuteBoundedAsync(
                token => _inner.DisconnectAsync(token), _options.StopTimeout, cancellationToken).ConfigureAwait(false);
            _sessionConnected = false;
            lock (_stateGate)
            {
                _requestedCommand = DroneCommand.Stop;
                _confirmedCommand = DroneCommand.Stop;
                _speedPercent = 0;
            }
        }
    }

    private bool RealHardwareSafetyIsUnverified
        => _options.RealHardwareMode && !_options.ExternalAbruptFailureProtectionVerified;

    private async Task RequestStopWithRetriesAsync(CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt <= _options.StopRetryCount; attempt++)
        {
            try
            {
                await ExecuteBoundedAsync(
                    token => _inner.SendCommandAsync(DroneCommand.Stop, token), _options.StopTimeout, cancellationToken)
                    .ConfigureAwait(false);

                // IDroneController is Task-only and does not return an outcome.
                // Verify its software snapshot advanced to STOP; the real
                // HardwareDroneController throws if the lower IDroneHardware
                // result was Rejected, so a stale movement confirmation can
                // never be accepted as STOP completion.
                var status = await ExecuteBoundedAsync(
                    token => _inner.GetStatusAsync(token), _options.StopTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (status.State.ConfirmedCommand != DroneCommand.Stop)
                {
                    throw new InvalidOperationException(
                        "The controller completed STOP without reporting a software STOP confirmation.");
                }

                lock (_stateGate)
                {
                    _requestedCommand = DroneCommand.Stop;
                    _confirmedCommand = DroneCommand.Stop;
                }
                Emit(DroneSafetyReason.StopRequested, DroneSafetyState.StopPending,
                    "Semantic STOP request completed at software level; physical result is unverified.");
                return;
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                lastFailure = exception;
                Emit(exception is DroneUnavailableException ? DroneSafetyReason.HardwareUnavailable : DroneSafetyReason.HardwareException, DroneSafetyState.StopPending,
                    $"STOP attempt {attempt + 1} failed.", exception);
                if (attempt < _options.StopRetryCount)
                {
                    await DelayRetryAsync(_options.StopRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw lastFailure ?? new DroneUnavailableException();
    }

    private async Task<bool> TryStopAfterFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_sessionConnected)
            {
                return false;
            }

            await RequestStopWithRetriesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsOperationalFailure(exception))
        {
            Emit(exception is DroneUnavailableException ? DroneSafetyReason.HardwareUnavailable : DroneSafetyReason.HardwareException, DroneSafetyState.Faulted,
                "Bounded STOP attempts were exhausted.", exception);
            return false;
        }
    }

    private async Task WatchMovementAsync(
        long generation,
        CancellationToken cancellationToken,
        TimeSpan remainingLiveness)
    {
        try
        {
            await Task.Delay(remainingLiveness, cancellationToken).ConfigureAwait(false);
            await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (generation != Interlocked.Read(ref _generation)
                    || CurrentSafetyState != DroneSafetyState.CommandActive)
                {
                    return;
                }

                SetState(DroneSafetyState.StopPending, DroneSafetyReason.CommandTimeout,
                    "Movement liveness deadline expired; requesting semantic STOP.");
                var stopped = await TryStopAfterFailureAsync(CancellationToken.None).ConfigureAwait(false);
                SetState(stopped ? DroneSafetyState.Safe : DroneSafetyState.Faulted,
                    DroneSafetyReason.CommandTimeout,
                    stopped
                        ? "Liveness timeout STOP completed at software level; physical stop is unverified."
                        : "Liveness timeout STOP failed; movement remains blocked.");
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer command, STOP, or shutdown superseded this watchdog.
        }
        catch (Exception exception) when (IsOperationalFailure(exception))
        {
            SetState(DroneSafetyState.Faulted, DroneSafetyReason.HardwareException,
                "Safety watchdog failed; movement is blocked.", exception);
        }
    }

    private void CancelMovementLease()
    {
        var lease = Interlocked.Exchange(ref _movementLease, null);
        if (lease is null)
        {
            return;
        }

        try
        {
            lease.Cancel();
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task<T> ExecuteBoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        var task = operation(linked.Token);
        try
        {
            return await task.WaitAsync(timeout, callerToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            ObserveLateCompletion(task);
            throw;
        }
    }

    private async Task ExecuteBoundedAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        var task = operation(linked.Token);
        try
        {
            await task.WaitAsync(timeout, callerToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            ObserveLateCompletion(task);
            throw;
        }
    }

    private static async Task DelayRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BestEffortDisconnectAsync()
    {
        try
        {
            await ExecuteBoundedAsync(
                token => _inner.DisconnectAsync(token), _options.StopTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsOperationalFailure(exception))
        {
            Emit(DroneSafetyReason.HardwareException, DroneSafetyState.Faulted,
                "Best-effort controller disconnect failed.", exception);
        }
        finally
        {
            _sessionConnected = false;
            ResetCommandSnapshot();
        }
    }

    private void ResetCommandSnapshot()
    {
        lock (_stateGate)
        {
            _requestedCommand = DroneCommand.Stop;
            _confirmedCommand = DroneCommand.Stop;
            _speedPercent = 0;
        }
    }

    private void SetState(DroneSafetyState state, DroneSafetyReason reason, string message, Exception? exception = null)
    {
        lock (_stateGate)
        {
            _state = state;
        }

        Emit(reason, state, message, exception);
    }

    private void Emit(DroneSafetyReason reason, DroneSafetyState state, string message, Exception? exception = null)
    {
        try
        {
            _emit(new DroneSafetyEvent(reason, state, DateTimeOffset.UtcNow, message, exception?.GetType().FullName));
        }
        catch
        {
            // A diagnostics sink failure must never disable safety processing.
        }
    }

    private static bool IsOperationalFailure(Exception exception)
        => exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    private static void ObserveLateCompletion(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
