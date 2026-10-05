namespace DroneControl.Infrastructure;

/// <summary>
/// Isolated port to the concrete Raspberry/Linux PWM implementation
/// (specs/hardware/pwm.md). Task 30 defines this boundary only; selection of
/// a Linux PWM subsystem, kernel API, device path, and permissions is deferred
/// until target inventory/runtime work — no subsystem is named, no device
/// path is assumed, and no default frequency or duty value exists here.
/// Implementors must use only explicitly configured identifiers and must
/// dispose any owned platform resources deterministically.
/// </summary>
public interface IRaspberryPwmPlatform : IDisposable
{
    /// <summary>Configures a supplied identifier as a PWM output at the given frequency.</summary>
    void ConfigureOutput(int identifier, int frequencyHz);

    /// <summary>Writes the supplied duty percent (0-100) unchanged to a configured output.</summary>
    void SetDutyCycle(int identifier, int dutyPercent);
}
