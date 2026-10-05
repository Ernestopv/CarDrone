namespace DroneControl.Infrastructure;

/// <summary>
/// Deterministic PC/test implementation of <see cref="IGpioController"/>.
/// It performs no OS or hardware calls and records requested line operations
/// only; recorded Low/High values do not imply electrical or motor behavior.
/// </summary>
public sealed class MockGpioController : IGpioController
{
    private readonly object _gate = new();
    private readonly HashSet<int> _configuredIdentifiers;
    private readonly HashSet<int> _configuredOutputs = [];
    private readonly Dictionary<int, GpioPinValue> _latestValues = [];
    private bool _disposed;

    public MockGpioController(GpioPinConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuredIdentifiers = configuration.DigitalLineIdentifiers.ToHashSet();
    }

    /// <summary>Returns a snapshot of configured output identifiers.</summary>
    public IReadOnlySet<int> ConfiguredOutputs
    {
        get
        {
            lock (_gate)
            {
                return _configuredOutputs.ToHashSet();
            }
        }
    }

    /// <summary>Returns a snapshot of the latest requested values for written lines.</summary>
    public IReadOnlyDictionary<int, GpioPinValue> LatestValues
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, GpioPinValue>(_latestValues);
            }
        }
    }

    public void ConfigureOutput(int pin)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureConfiguredIdentifier(pin);
            _configuredOutputs.Add(pin);
        }
    }

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

            _latestValues[pin] = value;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void EnsureConfiguredIdentifier(int pin)
    {
        if (!_configuredIdentifiers.Contains(pin))
        {
            throw new ArgumentException($"GPIO identifier {pin} is not present in the validated configuration.", nameof(pin));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
