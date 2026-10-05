using DroneControl.Domain;

namespace DroneControl.Application;

/// <summary>
/// Downstream drone abstraction consumed by <see cref="DroneService"/>; its
/// implementation arrives in a later task. The controller owns connection,
/// camera, requested/confirmed command, speed, and Raspberry Pi state. It does
/// not own the <c>Api</c> field of the returned status.
/// </summary>
public interface IDroneController
{
    /// <summary>
    /// Returns the controller-owned status. The <c>Api</c> value is irrelevant
    /// to callers because <see cref="DroneService"/> overwrites it.
    /// </summary>
    Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Connects to the drone.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects from the drone.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Executes a semantic command and acknowledges it.</summary>
    Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the speed percentage (0-100). Out-of-range values throw
    /// <see cref="ArgumentOutOfRangeException"/>; the value is never clamped.
    /// </summary>
    Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default);
}
