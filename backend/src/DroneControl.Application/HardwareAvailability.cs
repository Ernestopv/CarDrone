namespace DroneControl.Application;

/// <summary>
/// Presence of the drone hardware layer as seen through
/// <see cref="IDroneHardware"/>. Deliberately coarse: rich health detail is a
/// later provider concern.
/// </summary>
public enum HardwareAvailability
{
    /// <summary>
    /// The hardware layer cannot be reached or used. The default value is
    /// intentionally the pessimistic one: an unset/failed read never reads as
    /// "present".
    /// </summary>
    Unavailable = 0,

    /// <summary>The hardware layer is present and accepts operations.</summary>
    Available = 1,
}
