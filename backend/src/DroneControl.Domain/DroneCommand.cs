namespace DroneControl.Domain;

/// <summary>
/// Semantic drone movement commands. Commands carry no hardware meaning;
/// the mapping to GPIO happens outside the Domain.
/// </summary>
public enum DroneCommand
{
    /// <summary>Move forward.</summary>
    Forward,

    /// <summary>Move backward.</summary>
    Backward,

    /// <summary>Move left.</summary>
    Left,

    /// <summary>Move right.</summary>
    Right,

    /// <summary>Stop all movement. The only stop mechanism in the system.</summary>
    Stop
}
