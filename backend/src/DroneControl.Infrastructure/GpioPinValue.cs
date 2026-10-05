namespace DroneControl.Infrastructure;

/// <summary>
/// Digital level requested for a configured GPIO line. These are transport-level
/// line values only; they do not imply motor, enable, or electrical behavior.
/// </summary>
public enum GpioPinValue
{
    Low = 0,
    High = 1,
}
