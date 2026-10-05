namespace DroneControl.Domain;

/// <summary>
/// Immutable system-level status reported to upper layers. Default construction
/// yields the initial status: initial <see cref="DroneState"/>, Raspberry Pi
/// offline, API offline.
/// </summary>
public sealed record DroneStatus
{
    /// <summary>Drone subsystem snapshot.</summary>
    public DroneState State { get; init; } = new();

    /// <summary>Raspberry Pi availability.</summary>
    public ConnectionStatus RaspberryPi { get; init; } = ConnectionStatus.Offline;

    /// <summary>
    /// Backend/API availability as reported to clients. The Domain assumes nothing
    /// about hosting; reporting layers set this to
    /// <see cref="ConnectionStatus.Connected"/> when they serve status responses.
    /// </summary>
    public ConnectionStatus Api { get; init; } = ConnectionStatus.Offline;
}
