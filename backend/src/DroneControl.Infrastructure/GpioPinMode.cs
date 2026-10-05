namespace DroneControl.Infrastructure;

/// <summary>
/// GPIO line mode supported by this abstraction. Only output is included because
/// the active specification has no input consumer.
/// </summary>
public enum GpioPinMode
{
    Output = 0,
}
