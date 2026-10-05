namespace DroneControl.Infrastructure;

/// <summary>
/// Infrastructure-only seam for configuring and writing digital GPIO outputs.
/// Implementations receive validated configuration and expose no motor, camera,
/// PWM, or application-command semantics.
/// </summary>
public interface IGpioController : IDisposable
{
    /// <summary>Configures one of the supplied identifiers as a digital output.</summary>
    void ConfigureOutput(int pin);

    /// <summary>Writes a transport-level digital value to a configured output.</summary>
    void Write(int pin, GpioPinValue value);
}
