namespace DroneControl.Application;

/// <summary>
/// Thrown when a drone command arrives while the controller is not connected.
/// A stable, specific type for the API layer to map: unlike
/// <see cref="InvalidOperationException"/>, it can only originate from the
/// command-while-disconnected rule.
/// </summary>
public sealed class DroneNotConnectedException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public DroneNotConnectedException()
        : base("The drone is not connected.")
    {
    }
}
