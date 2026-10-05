using System.Globalization;

namespace DroneControl.Infrastructure;

/// <summary>
/// Validated identifiers for the four configured digital lines. The property
/// names are configuration identifiers only; this type assigns no motor or
/// electrical meaning. PWM1/PWM2 are deliberately not consumed here: the
/// Task 30 <c>PwmMapping</c> binding reads them from the same GPIO section.
/// </summary>
public sealed record GpioPinConfiguration
{
    private GpioPinConfiguration(int pin1, int pin2, int pin3, int pin4)
    {
        Pin1 = pin1;
        Pin2 = pin2;
        Pin3 = pin3;
        Pin4 = pin4;
    }

    public int Pin1 { get; }
    public int Pin2 { get; }
    public int Pin3 { get; }
    public int Pin4 { get; }

    /// <summary>Returns the configured digital line identifiers in stable name order.</summary>
    public IReadOnlyList<int> DigitalLineIdentifiers => [Pin1, Pin2, Pin3, Pin4];

    /// <summary>
    /// Binds the values of the existing GPIO configuration section. The caller
    /// supplies section-relative keys (Pin1...Pin4); absent values never fall
    /// back to the example numbers in AGENTS.md. If a verified platform API
    /// provides a supported-identifier set, pass it to reject unsupported
    /// lines; no platform-specific range is invented here.
    /// </summary>
    public static GpioPinConfiguration FromSection(
        IReadOnlyDictionary<string, string?> values,
        IReadOnlySet<int>? supportedLineIdentifiers = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        var pin1 = ParseRequired(values, nameof(Pin1));
        var pin2 = ParseRequired(values, nameof(Pin2));
        var pin3 = ParseRequired(values, nameof(Pin3));
        var pin4 = ParseRequired(values, nameof(Pin4));
        var pins = new[] { pin1, pin2, pin3, pin4 };

        if (pins.Any(pin => pin < 0))
        {
            throw new ArgumentException("GPIO line identifiers must be non-negative.", nameof(values));
        }

        if (pins.Distinct().Count() != pins.Length)
        {
            throw new ArgumentException("GPIO Pin1 through Pin4 identifiers must be distinct.", nameof(values));
        }

        if (supportedLineIdentifiers is not null)
        {
            var unsupported = pins.Where(pin => !supportedLineIdentifiers.Contains(pin)).Distinct().ToArray();
            if (unsupported.Length > 0)
            {
                throw new ArgumentException(
                    $"GPIO line identifier(s) are unsupported by the selected interface: {string.Join(", ", unsupported)}.",
                    nameof(values));
            }
        }

        return new GpioPinConfiguration(pin1, pin2, pin3, pin4);
    }

    private static int ParseRequired(IReadOnlyDictionary<string, string?> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException($"Required GPIO configuration value '{key}' is missing.", nameof(values));
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"GPIO configuration value '{key}' must be an integer.", nameof(values));
        }

        return parsed;
    }
}
