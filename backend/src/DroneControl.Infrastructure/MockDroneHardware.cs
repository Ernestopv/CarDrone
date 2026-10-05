using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Software reference implementation of <see cref="IDroneHardware"/>:
/// deterministic, immediate, and free of any platform or device dependency,
/// so it behaves identically on every OS (specs/hardware/hardware-abstraction.md).
/// It models a healthy, reachable layer — presence is hard-wired
/// <see cref="HardwareAvailability.Available"/>; the unavailable path of the
/// contract is asserted by tests through explicit fakes, never by adding
/// failure injection here. The recorded properties exist purely so tests and
/// diagnostics can inspect this software layer; they are not part of the
/// interface upper layers consume.
/// </summary>
public sealed class MockDroneHardware : IDroneHardware
{
    private readonly object _gate = new();
    private DroneCommand? _lastCommand;
    private int _lastSpeedPercent;

    /// <summary>The most recent command applied through this layer, if any.</summary>
    public DroneCommand? LastAppliedCommand
    {
        get
        {
            lock (_gate)
            {
                return _lastCommand;
            }
        }
    }

    /// <summary>The most recent speed (0-100) applied through this layer.</summary>
    public int LastAppliedSpeedPercent
    {
        get
        {
            lock (_gate)
            {
                return _lastSpeedPercent;
            }
        }
    }

    /// <inheritdoc />
    public Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HardwareAvailability.Available);
    }

    /// <inheritdoc />
    public Task<CommandExecutionResult> ExecuteCommandAsync(
        DroneCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _lastCommand = command;
        }

        // Simulated software-level execution only — nothing physical is
        // implied by Applied (see CommandExecutionResult docs).
        return Task.FromResult(new CommandExecutionResult(HardwareCommandStatus.Applied));
    }

    /// <inheritdoc />
    public Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (speedPercent is < 0 or > 100)
        {
            // Reject, never clamp — same invariant and message style every
            // other layer uses (domain-model spec).
            throw new ArgumentOutOfRangeException(
                nameof(speedPercent),
                speedPercent,
                "Speed must be between 0 and 100.");
        }

        lock (_gate)
        {
            _lastSpeedPercent = speedPercent;
        }

        return Task.CompletedTask;
    }
}
