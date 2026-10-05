using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// The hardware-backed <see cref="IDroneController"/> (decision D3 of
/// specs/architecture/runtime-deployment.md: the provider stays BELOW the
/// controller contract). Session truth, no simulation: availability comes from
/// probing <see cref="IDroneHardware"/>, commands are confirmed only when the
/// hardware layer reports a software-level Applied outcome, and there are no
/// artificial delays. An unreachable layer throws
/// <see cref="DroneUnavailableException"/> (existing 503 channel) instead of
/// pretending; nothing here can claim physical confirmation
/// (CommandExecutionResult semantics from Task 22).
/// </summary>
public sealed class HardwareDroneController : IDroneController
{
    private readonly IDroneHardware _hardware;
    private readonly object _gate = new();
    private bool _connected;
    private DroneCommand _requested = DroneCommand.Stop;
    private DroneCommand _confirmed = DroneCommand.Stop;
    private int _speedPercent;

    /// <summary>Creates the bridge over the hardware seam.</summary>
    public HardwareDroneController(IDroneHardware hardware)
    {
        _hardware = hardware;
    }

    /// <inheritdoc />
    public async Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var availability = await _hardware.GetAvailabilityAsync(cancellationToken);

        lock (_gate)
        {
            return new DroneStatus
            {
                State = new DroneState
                {
                    Connection = _connected ? ConnectionStatus.Connected : ConnectionStatus.Offline,
                    // Camera in hardware graphs: honest Offline whenever
                    // CAMERA_MODE=mock (no pipeline exists — never a
                    // simulated stream). Under CAMERA_MODE=ustreamer the
                    // composed Task 31 overlay replaces this value with
                    // observed pipeline health (camera-runtime.md).
                    Camera = CameraStatus.Offline,
                    RequestedCommand = _requested,
                    ConfirmedCommand = _confirmed,
                    Speed = _speedPercent,
                },
                // The Raspberry Pi field reflects the probed hardware layer only.
                RaspberryPi = availability == HardwareAvailability.Available
                    ? ConnectionStatus.Connected
                    : ConnectionStatus.Offline,
                // Api stays unstamped: DroneService owns that (Task 10 rule).
            };
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // The hardware layer is the reachability truth: no availability, no session.
        var availability = await _hardware.GetAvailabilityAsync(cancellationToken);
        if (availability != HardwareAvailability.Available)
        {
            throw new DroneUnavailableException();
        }

        lock (_gate)
        {
            _connected = true;
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _connected = false;
            _requested = DroneCommand.Stop;
            _confirmed = DroneCommand.Stop;
            _speedPercent = 0;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_connected)
            {
                throw new DroneNotConnectedException();
            }

            // A request records intent only; confirmation comes from the layer.
            _requested = command;
        }

        var result = await _hardware.ExecuteCommandAsync(command, cancellationToken);
        if (result.Status == HardwareCommandStatus.Applied)
        {
            lock (_gate)
            {
                _confirmed = command;
            }

            return;
        }

        // The public IDroneController contract is Task-only. Preserve that
        // contract by surfacing a software rejection as a failure rather than
        // returning success that a safety layer might mistake for STOP success.
        // The existing confirmed command remains untouched.
        throw new InvalidOperationException(
            result.Reason ?? "The hardware layer rejected the semantic command.");
    }

    /// <inheritdoc />
    public async Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
    {
        if (speed is < 0 or > 100)
        {
            // Application invariant first — illegal is illegal in every mode.
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                speed,
                "Speed must be between 0 and 100.");
        }

        lock (_gate)
        {
            if (!_connected)
            {
                // Wire parity with the simulator: while disconnected, speed
                // silently no-ops and is never forwarded (nothing is claimed).
                return;
            }
        }

        await _hardware.ApplySpeedAsync(speed, cancellationToken);

        lock (_gate)
        {
            _speedPercent = speed;
        }
    }
}
