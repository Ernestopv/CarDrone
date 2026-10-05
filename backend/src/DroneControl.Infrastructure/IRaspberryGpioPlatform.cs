namespace DroneControl.Infrastructure;

/// <summary>
/// Isolated port to the concrete Raspberry/Linux GPIO implementation. Task 25
/// defines this boundary only; selection of a library, kernel API, device path,
/// and permissions is deferred until target inventory/runtime work. Implementors
/// must use only explicitly configured line identifiers and must dispose any
/// owned platform resources deterministically.
/// </summary>
public interface IRaspberryGpioPlatform : IDisposable
{
    /// <summary>Configures a supplied line identifier as a digital output.</summary>
    void ConfigureOutput(int lineIdentifier);

    /// <summary>Writes the supplied digital level unchanged to a configured output.</summary>
    void Write(int lineIdentifier, GpioPinValue value);
}
