namespace DroneControl.Infrastructure;

/// <summary>
/// Infrastructure adapter from the generic GPIO seam to an injected
/// Raspberry/Linux platform port. This class has no GPIO library, kernel API,
/// device path, permission, motor, PWM, or camera knowledge. The concrete
/// <see cref="IRaspberryGpioPlatform"/> implementation is selected later when
/// target-specific GPIO inventory is available.
/// </summary>
public sealed class RaspberryGpioController : IGpioController
{
    private readonly object _gate = new();
    private readonly GpioPinConfiguration _configuration;
    private readonly IRaspberryGpioPlatform _platform;
    private readonly HashSet<int> _configuredOutputs = [];
    private bool _disposed;

    /// <summary>Creates the adapter from validated identifiers and a platform port.</summary>
    public RaspberryGpioController(
        GpioPinConfiguration configuration,
        IRaspberryGpioPlatform platform)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    /// <inheritdoc />
    public void ConfigureOutput(int pin)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureConfiguredIdentifier(pin);

            // Record only after the platform operation succeeds.
            _platform.ConfigureOutput(pin);
            _configuredOutputs.Add(pin);
        }
    }

    /// <inheritdoc />
    public void Write(int pin, GpioPinValue value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown GPIO digital value.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureConfiguredIdentifier(pin);
            if (!_configuredOutputs.Contains(pin))
            {
                throw new InvalidOperationException($"GPIO identifier {pin} is not configured as an output.");
            }

            // The transport value is forwarded verbatim; no motor semantics.
            _platform.Write(pin, value);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Mark disposed before delegating so use-after-dispose is
            // deterministic even if platform cleanup itself throws.
            _disposed = true;
            _platform.Dispose();
        }
    }

    private void EnsureConfiguredIdentifier(int pin)
    {
        if (!_configuration.DigitalLineIdentifiers.Contains(pin))
        {
            throw new ArgumentException($"GPIO identifier {pin} is not present in the validated configuration.", nameof(pin));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
