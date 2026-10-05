using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// In-memory simulation of the drone controller. Owns the simulated drone,
/// camera, speed, requested/confirmed command and Raspberry Pi state as a
/// stateful singleton. Uses no hardware, network or operating-system device
/// APIs — only state transitions and timer delays mirroring the Phase 1
/// frontend mock (700 ms connection, 250 ms acknowledgement). A confirmed
/// command means the simulator confirmed it, never physical hardware.
/// </summary>
public sealed class MockDroneController : IDroneController
{
    private const int ConnectionDelayMs = 700;
    private const int CommandAckDelayMs = 250;

    /// <summary>
    /// Guards every state field. Never held across an await, so status reads
    /// respond while a simulated delay is in flight.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// Serializes command confirmations so overlapping commands are confirmed
    /// in request order. A semaphore, not the state gate: it blocks only other
    /// command senders, never status reads.
    /// </summary>
    private readonly SemaphoreSlim _confirmationGate = new(1, 1);

    private DroneState _state = new();
    private ConnectionStatus _raspberryPi = ConnectionStatus.Offline;

    /// <summary>
    /// Bumped by every disconnect. A pending connection attempt or command
    /// acknowledgement captures the epoch when it starts and skips applying its
    /// state when the value no longer matches (superseded by a disconnect).
    /// </summary>
    private long _epoch;

    /// <inheritdoc />
    public Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // DroneState and DroneStatus are immutable records, so the captured
            // references are consistent snapshots. Api is deliberately left at
            // its default; DroneService stamps it.
            return Task.FromResult(new DroneStatus
            {
                State = _state,
                RaspberryPi = _raspberryPi,
            });
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        long attempt;
        lock (_gate)
        {
            if (_state.Connection != ConnectionStatus.Offline)
            {
                // Already connecting or connected: idempotent no-op so a
                // repeated connect can never reset a live connection.
                return;
            }

            // Applied before the first asynchronous suspension so an immediate
            // status read observes the attempt (frontend parity: drone, camera
            // and services all report "connecting").
            _state = _state with
            {
                Connection = ConnectionStatus.Connecting,
                Camera = CameraStatus.Connecting,
            };
            _raspberryPi = ConnectionStatus.Connecting;
            attempt = _epoch;
        }

        try
        {
            await Task.Delay(ConnectionDelayMs, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (_epoch == attempt && _state.Connection == ConnectionStatus.Connecting)
                {
                    // Cancelled attempt: return to fully initial state rather
                    // than staying stuck in Connecting.
                    _state = new DroneState();
                    _raspberryPi = ConnectionStatus.Offline;
                }
            }

            throw;
        }

        lock (_gate)
        {
            if (_epoch != attempt)
            {
                // Superseded by a disconnect while the attempt was in flight;
                // the reset wins and the connection is never resurrected.
                return;
            }

            _state = _state with
            {
                Connection = ConnectionStatus.Connected,
                Camera = CameraStatus.Streaming,
            };
            _raspberryPi = ConnectionStatus.Connected;
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Immediate, unconditional reset (frontend parity) that also
            // supersedes any in-flight connection attempt or confirmation.
            _epoch++;
            _state = new DroneState();
            _raspberryPi = ConnectionStatus.Offline;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
    {
        long issued;
        lock (_gate)
        {
            if (_state.Connection != ConnectionStatus.Connected)
            {
                throw new DroneNotConnectedException();
            }

            // Requested before the first suspension: a request records intent
            // only and never confirms (Task 10 rule).
            _state = _state with { RequestedCommand = command };
            issued = _epoch;
        }

        await _confirmationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Delay(CommandAckDelayMs, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (_epoch == issued && _state.Connection == ConnectionStatus.Connected)
                {
                    // Simulated acknowledgement only — this is not hardware
                    // confirmation. Skipped if a disconnect reset the state
                    // while the acknowledgement was pending.
                    _state = _state with { ConfirmedCommand = command };
                }
            }
        }
        finally
        {
            _confirmationGate.Release();
        }
    }

    /// <inheritdoc />
    public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state.Connection != ConnectionStatus.Connected)
            {
                // Parity with the frontend mock: speed changes while offline
                // are ignored. The connection gate runs before the range check,
                // so an out-of-range value while disconnected is also a no-op.
                return Task.CompletedTask;
            }

            // The record initializer validates the range: an out-of-range value
            // throws ArgumentOutOfRangeException before the assignment happens,
            // so state stays unchanged and speed is never clamped.
            _state = _state with { Speed = speed };
        }

        return Task.CompletedTask;
    }
}
