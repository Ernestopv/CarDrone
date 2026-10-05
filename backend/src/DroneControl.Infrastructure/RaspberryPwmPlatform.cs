using System.Globalization;

namespace DroneControl.Infrastructure;

/// <summary>
/// Concrete Linux PWM platform operating the kernel's PWM sysfs interface
/// (<c>/sys/class/pwm/pwmchipN/…/export, period, duty_cycle, enable</c>). No
/// subsystem is assumed beyond the documented sysfs ABI, and no third-party
/// package is used. The chip path and the identifier→channel mapping come from
/// configuration at the composition root — the channel numbers are operator
/// evidence from the target (nothing is invented here); an identifier without a
/// mapping refuses loudly (it can never silently write to a wrong channel).
/// <para>
/// When the chip is missing or not accessible, operations throw with the
/// underlying IO/UnauthorizedAccess reason; the provider surfaces it through
/// the existing operational-failure channel (honest Unavailable/503). Dispose
/// deterministically drives the configured outputs to duty 0 and disables
/// them, mirroring the upstream SpeedController contract.
/// </para>
/// </summary>
public sealed class RaspberryPwmPlatform : IRaspberryPwmPlatform
{
    private readonly object _gate = new();
    private readonly string _chipPath;
    private readonly IReadOnlyDictionary<int, int> _channelByIdentifier;
    private readonly Dictionary<int, long> _periodNsByIdentifier = [];
    private readonly HashSet<int> _configured = [];
    private bool _disposed;

    /// <summary>A validated chip path and a complete identifier→channel map are required.</summary>
    public RaspberryPwmPlatform(string chipPath, IReadOnlyDictionary<int, int> channelByIdentifier)
    {
        _chipPath = string.IsNullOrWhiteSpace(chipPath)
            ? throw new ArgumentException("A PWM chip path is required.", nameof(chipPath))
            : chipPath;
        _channelByIdentifier = channelByIdentifier
            ?? throw new ArgumentNullException(nameof(channelByIdentifier));
    }

    /// <inheritdoc />
    public void ConfigureOutput(int identifier, int frequencyHz)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var channel = ChannelOf(identifier);
            var outputDir = OutputPath(channel);
            var chipPath = _chipPath;

            if (!Directory.Exists(outputDir))
            {
                // Exporting creates pwm{channel}; a concurrent/previous export
                // already existing is fine (EEXIST-equivalent, no op).
                WriteSysfs(Path.Combine(chipPath, "export"), channel.ToString(CultureInfo.InvariantCulture));
            }

            var periodNs = 1_000_000_000L / checked((long)frequencyHz);
            WriteSysfs(Path.Combine(outputDir, "period"), periodNs.ToString(CultureInfo.InvariantCulture));
            WriteSysfs(Path.Combine(outputDir, "duty_cycle"), "0");
            WriteSysfs(Path.Combine(outputDir, "enable"), "0");

            _periodNsByIdentifier[identifier] = periodNs;
            _configured.Add(identifier);
        }
    }

    /// <inheritdoc />
    public void SetDutyCycle(int identifier, int dutyPercent)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_configured.Contains(identifier))
            {
                throw new InvalidOperationException($"PWM identifier {identifier} is not configured as an output.");
            }

            var channel = ChannelOf(identifier);
            var outputDir = OutputPath(channel);
            var dutyNs = _periodNsByIdentifier[identifier] * dutyPercent / 100;

            WriteSysfs(Path.Combine(outputDir, "duty_cycle"), dutyNs.ToString(CultureInfo.InvariantCulture));
            WriteSysfs(Path.Combine(outputDir, "enable"), "1");
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

            _disposed = true;
            // Deterministic output-0 shutdown: drive duty 0 then disable.
            foreach (var identifier in _configured)
            {
                var outputDir = OutputPath(ChannelOf(identifier));
                WriteSysfs(Path.Combine(outputDir, "duty_cycle"), "0");
                WriteSysfs(Path.Combine(outputDir, "enable"), "0");
            }

            _configured.Clear();
        }
    }

    private int ChannelOf(int identifier)
    {
        if (!_channelByIdentifier.TryGetValue(identifier, out var channel))
        {
            throw new ArgumentException(
                $"PWM identifier {identifier} has no configured Raspberry:PWM:Channels mapping.",
                nameof(identifier));
        }

        return channel;
    }

    private string OutputPath(int channel)
        => Path.Combine(_chipPath, $"pwm{channel.ToString(CultureInfo.InvariantCulture)}");

    private static void WriteSysfs(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"PWM sysfs write failed for '{path}' with '{value}': {exception.Message}", exception);
        }
    }
}