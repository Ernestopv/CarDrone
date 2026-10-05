namespace DroneControl.Infrastructure;

/// <summary>
/// Infrastructure adapter from the generic PWM seam to an injected
/// Raspberry/Linux platform port. This class has no PWM library, kernel API,
/// device path, permission, speed, motor, or L298N knowledge — frequency and
/// duty are opaque validated parameters forwarded unchanged. The concrete
/// <see cref="IRaspberryPwmPlatform"/> implementation is selected later when
/// target-specific PWM inventory is available.
/// </summary>
public sealed class RaspberryPwmController : IPwmController
{
    private readonly object _gate = new();
    private readonly HashSet<int> _configuredIdentifiers;
    private readonly IRaspberryPwmPlatform _platform;
    private readonly HashSet<int> _configuredOutputs = [];
    private bool _disposed;

    /// <summary>Creates the adapter from validated identifiers and a platform port.</summary>
    public RaspberryPwmController(
        IReadOnlyCollection<int> configuredIdentifiers,
        IRaspberryPwmPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(configuredIdentifiers);
        _configuredIdentifiers = configuredIdentifiers.ToHashSet();
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    /// <inheritdoc />
    public void ConfigureOutput(int identifier, int frequencyHz)
    {
        if (frequencyHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frequencyHz),
                frequencyHz,
                "PWM frequency must be a positive integer.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureConfiguredIdentifier(identifier);

            // Forward only after validation; membership is tracked only after
            // the platform operation succeeds.
            _platform.ConfigureOutput(identifier, frequencyHz);
            _configuredOutputs.Add(identifier);
        }
    }

    /// <inheritdoc />
    public void SetDutyCycle(int identifier, int dutyPercent)
    {
        if (dutyPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dutyPercent),
                dutyPercent,
                "PWM duty percent must be between 0 and 100.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureConfiguredIdentifier(identifier);
            if (!_configuredOutputs.Contains(identifier))
            {
                throw new InvalidOperationException(
                    $"PWM identifier {identifier} is not configured as an output.");
            }

            // The transport value is forwarded verbatim; no speed semantics.
            _platform.SetDutyCycle(identifier, dutyPercent);
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

    private void EnsureConfiguredIdentifier(int identifier)
    {
        if (!_configuredIdentifiers.Contains(identifier))
        {
            throw new ArgumentException(
                $"PWM identifier {identifier} is not present in the validated configuration.",
                nameof(identifier));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
