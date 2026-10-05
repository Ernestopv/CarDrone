using DroneControl.Domain;

namespace DroneControl.Application;

/// <summary>
/// Application-level service abstraction between the API and the drone.
/// </summary>
public interface IDroneService
{
    /// <summary>
    /// Returns the current system status with the API component marked connected.
    /// </summary>
    Task<DroneStatus> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Connects to the drone.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects from the drone.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Forwards a semantic command to the drone controller.</summary>
    Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the speed percentage (0-100). Out-of-range values throw
    /// <see cref="ArgumentOutOfRangeException"/>; the value is never clamped.
    /// </summary>
    Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default);
}
