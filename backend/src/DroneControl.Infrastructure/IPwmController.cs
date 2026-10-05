namespace DroneControl.Infrastructure;

/// <summary>
/// Infrastructure-only seam for configuring and driving PWM outputs
/// (specs/hardware/pwm.md). The shape mirrors <see cref="IGpioController"/>
/// (configure-then-write): an identifier is configured with a frequency first,
/// then duty cycles are written to it. Implementations receive validated
/// configuration and expose no speed, motor, L298N, or application-command
/// semantics. The GPIO seam stays digital-only and is not extended.
/// </summary>
public interface IPwmController : IDisposable
{
    /// <summary>Configures one of the supplied identifiers as a PWM output at the given frequency.</summary>
    void ConfigureOutput(int identifier, int frequencyHz);

    /// <summary>Writes a duty percent (0-100) to a configured PWM output.</summary>
    void SetDutyCycle(int identifier, int dutyPercent);
}
