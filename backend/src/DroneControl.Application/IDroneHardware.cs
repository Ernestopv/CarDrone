using DroneControl.Domain;

namespace DroneControl.Application;

/// <summary>
/// Application-level hardware seam (specs/hardware/hardware-abstraction.md):
/// the software contract for "is the hardware layer present, apply this
/// semantic command, apply this speed". Selection between implementations
/// happens exclusively at the composition root; layers above never know what
/// backs the seam, and nothing above it may reference hardware specifics.
/// This seam deliberately has no connection state machine and no
/// acknowledgement concept — those belong to <see cref="IDroneController"/>.
/// </summary>
public interface IDroneHardware
{
    /// <summary>
    /// Reports whether the hardware layer is present and can receive
    /// operations. Rich diagnostics are a future provider concern; this
    /// answers only the presence question.
    /// </summary>
    Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a semantic command to the hardware layer and returns the
    /// software-level outcome. Contract rule: when the hardware is
    /// unavailable, implementations throw
    /// <see cref="DroneUnavailableException"/> — they never reject quietly
    /// and never silently no-op (fail-fast, decision D5).
    /// </summary>
    Task<CommandExecutionResult> ExecuteCommandAsync(
        DroneCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a speed percentage (0-100). Out-of-range values throw
    /// <see cref="ArgumentOutOfRangeException"/> and are never clamped —
    /// the same invariant every upper layer already enforces. When the
    /// hardware is unavailable, throws
    /// <see cref="DroneUnavailableException"/> like commands.
    /// </summary>
    Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default);
}
