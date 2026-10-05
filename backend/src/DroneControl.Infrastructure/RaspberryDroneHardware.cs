using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Raspberry hardware provider (specs/hardware/raspberry-hardware-provider.md),
/// INERT by default: with no operator-asserted motor or PWM mapping the
/// provider truthfully reports <see cref="HardwareAvailability.Unavailable"/>
/// and every operation throws <see cref="DroneUnavailableException"/> (the
/// existing 503 channel — loud, never a silent no-op, per decision D5).
///
/// Two independent capabilities may be active:
/// - <see cref="MotorController"/> (Task 29 matrix: a complete, validated
///   mapping with <c>DirectionMappingVerified=true</c>) routes semantic
///   commands;
/// - <see cref="SpeedController"/> (Task 30 matrix: a valid envelope with
///   <c>SpeedMappingVerified=true</c>) routes speed application.
/// Either active capability makes the provider report software-level
/// presence; the other capability keeps its explicit refusal — a
/// capability-specific <see cref="DroneUnavailableException"/> naming what is
/// missing, never the default message and never a silent no-op. The concrete
/// Linux output sinks are still deferred by the prerequisites ledger
/// (specs/architecture/runtime-deployment.md), so an asserted mapping in
/// <c>real</c> mode aborts at the composition root instead of resolving here.
/// Nothing above the <see cref="IDroneHardware"/> seam changes either way.
/// </summary>
public sealed class RaspberryDroneHardware : IDroneHardware
{
    /// <summary>
    /// Why operations fail while the provider is inert. Surfaced through the
    /// startup warning in the composition root and in this type's tests; the
    /// exception itself keeps its established Application-layer message.
    /// </summary>
    internal const string InertReason =
        "Raspberry hardware actuation is not implemented yet; device access arrives with Task 24 and the line abstraction with Task 25.";

    /// <summary>
    /// Commands refused while only the speed capability is active: direction
    /// is not configured, and the surfaced 503 detail must say so instead of
    /// claiming the whole implementation is gone.
    /// </summary>
    internal const string DirectionUnavailableReason =
        "Motor direction control is not configured; commands require a valid MotorMapping section with DirectionMappingVerified=true.";

    /// <summary>
    /// Speed refused while only the motor capability is active: speed is
    /// never silently successful and never pretends to be applied — it names
    /// the missing PWM configuration instead.
    /// </summary>
    internal const string SpeedUnavailableReason =
        "Speed control is not configured; speed requires a valid PwmMapping section with SpeedMappingVerified=true.";

    private readonly MotorController? _motorController;
    private readonly SpeedController? _speedController;

    /// <summary>Inert provider: no active motor or PWM mapping was asserted.</summary>
    public RaspberryDroneHardware()
    {
    }

    /// <summary>
    /// Motor-only provider: semantic commands are applied through the
    /// asserted mapping; speed keeps its explicit refusal. Registration
    /// happens only in the composition root.
    /// </summary>
    public RaspberryDroneHardware(MotorController motorController)
        => _motorController = motorController ?? throw new ArgumentNullException(nameof(motorController));

    /// <summary>
    /// Capability-composition provider (composition-root factory): each
    /// argument may be <c>null</c>, meaning that capability's mapping was not
    /// asserted. At least one active capability makes the provider Available;
    /// both <c>null</c> reproduces the inert behavior exactly.
    /// </summary>
    public RaspberryDroneHardware(MotorController? motorController, SpeedController? speedController)
    {
        _motorController = motorController;
        _speedController = speedController;
    }

    /// <inheritdoc />
    public Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _motorController is null && _speedController is null
                ? HardwareAvailability.Unavailable
                : HardwareAvailability.Available);
    }

    /// <inheritdoc />
    public Task<CommandExecutionResult> ExecuteCommandAsync(
        DroneCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_motorController is null)
        {
            // Inert: the unchanged default message. Speed-only: the
            // capability-specific reason — presence never hides a missing
            // direction capability behind the default 503 text.
            return Task.FromException<CommandExecutionResult>(
                _speedController is null
                    ? new DroneUnavailableException()
                    : new DroneUnavailableException(DirectionUnavailableReason));
        }

        // Synchronous application: Applied is returned only after every
        // asserted output operation completed; any failure propagates and no
        // result is produced. Applied stays software-level — it never claims
        // physical actuation or hardware confirmation.
        _motorController.Apply(command);
        return Task.FromResult(new CommandExecutionResult(HardwareCommandStatus.Applied));
    }

    /// <inheritdoc />
    public Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Invariants run first: an illegal speed stays illegal regardless of
        // hardware state, matching every other layer (Task 22 contract).
        if (speedPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedPercent),
                speedPercent,
                "Speed must be between 0 and 100.");
        }

        if (_speedController is null)
        {
            // Explicit refusal in BOTH states (inert: the unchanged default
            // message; motor-only: naming the real reason so the surfaced 503
            // detail never claims the whole implementation is gone when only
            // speed is missing). Speed is never silently successful.
            return Task.FromException(
                _motorController is null
                    ? new DroneUnavailableException()
                    : new DroneUnavailableException(SpeedUnavailableReason));
        }

        // Synchronous application: success returns only after every intended
        // duty operation completed; any failure propagates and no success is
        // produced. It never claims physical actuation — the envelope is an
        // operator assertion, not verified electrical truth.
        _speedController.Apply(speedPercent, cancellationToken);
        return Task.CompletedTask;
    }
}
