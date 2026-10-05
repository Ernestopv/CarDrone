namespace DroneControl.Domain;

/// <summary>
/// Immutable snapshot of the drone subsystem. Default construction yields the
/// initial state: drone offline, camera offline, both commands
/// <see cref="DroneCommand.Stop"/>, speed 0. State changes produce new instances
/// through record <c>with</c> expressions.
/// </summary>
public sealed record DroneState
{
    private int _speed;

    /// <summary>Drone link status.</summary>
    public ConnectionStatus Connection { get; init; } = ConnectionStatus.Offline;

    /// <summary>Camera stream status.</summary>
    public CameraStatus Camera { get; init; } = CameraStatus.Offline;

    /// <summary>Last command requested by the caller. Records user intent only.</summary>
    public DroneCommand RequestedCommand { get; init; } = DroneCommand.Stop;

    /// <summary>
    /// Last command explicitly confirmed by the service. It never changes without
    /// confirmation; the Domain performs no confirmation itself.
    /// </summary>
    public DroneCommand ConfirmedCommand { get; init; } = DroneCommand.Stop;

    /// <summary>
    /// Speed percentage from 0 to 100 inclusive. The relationship to PWM output is
    /// defined later. Out-of-range values throw
    /// <see cref="ArgumentOutOfRangeException"/> at construction and on record
    /// <c>with</c> updates; the Domain never silently clamps.
    /// </summary>
    public int Speed
    {
        get => _speed;
        init => _speed = value is < 0 or > 100
            ? throw new ArgumentOutOfRangeException(
                nameof(Speed),
                value,
                "Speed must be between 0 and 100.")
            : value;
    }
}
