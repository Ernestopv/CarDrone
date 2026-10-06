using System.Globalization;

namespace DroneControl.Infrastructure;

/// <summary>
/// Validated configuration for the INA219 battery monitor. Paths, addresses and
/// the voltage window are operator values (or the target-observed bus default);
/// nothing here is an electrical measurement. The class deliberately carries no
/// Microsoft.Extensions.Configuration dependency — the composition root passes
/// section-relative key/value pairs.
/// </summary>
public sealed record BatteryOptions
{
    /// <summary>Target-observed I2C bus node (docs/hardware/raspberry-pi-inventory.md).</summary>
    public const string DefaultI2cBusPath = "/dev/i2c-1";

    /// <summary>Default low-level threshold (presentation semantics, not hardware).</summary>
    public const int DefaultLowPercent = 20;

    /// <summary>Default critical-level threshold (presentation semantics, not hardware).</summary>
    public const int DefaultCriticalPercent = 10;

    private BatteryOptions(
        string i2cBusPath,
        int i2cAddress,
        double fullVoltage,
        double emptyVoltage,
        double shuntOhms,
        int lowPercent,
        int criticalPercent)
    {
        I2cBusPath = i2cBusPath;
        I2cAddress = i2cAddress;
        FullVoltage = fullVoltage;
        EmptyVoltage = emptyVoltage;
        ShuntOhms = shuntOhms;
        LowPercent = lowPercent;
        CriticalPercent = criticalPercent;
    }

    /// <summary>I2C bus character-device path.</summary>
    public string I2cBusPath { get; }

    /// <summary>7-bit I2C address of the INA219 module.</summary>
    public int I2cAddress { get; }

    /// <summary>Pack voltage corresponding to 100 % (operator value).</summary>
    public double FullVoltage { get; }

    /// <summary>Pack voltage corresponding to 0 % (operator value).</summary>
    public double EmptyVoltage { get; }

    /// <summary>
    /// Shunt resistance of the sensor board in ohms (operator evidence, e.g.
    /// <c>0.1</c> for a <c>R100</c> module). Used to convert the shunt voltage
    /// into current: <c>current = shuntVolts / ShuntOhms</c>.
    /// </summary>
    public double ShuntOhms { get; }

    /// <summary>Percent threshold at or below which the state is <c>Low</c>.</summary>
    public int LowPercent { get; }

    /// <summary>Percent threshold at or below which the state is <c>Critical</c>.</summary>
    public int CriticalPercent { get; }

    /// <summary>
    /// Binds and validates the <c>Battery:</c> section. Every violated rule is
    /// aggregated into a single <see cref="ArgumentException"/>; the caller
    /// converts it to a startup abort (D5). Absent values never fall back to
    /// invented voltages or an invented address — only the bus path and the
    /// presentation thresholds have documented defaults.
    /// </summary>
    public static BatteryOptions FromSection(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var errors = new List<string>();

        var busPath = DefaultI2cBusPath;
        var rawBus = Get(values, "I2cBusPath");
        if (rawBus is not null)
        {
            if (string.IsNullOrWhiteSpace(rawBus))
            {
                errors.Add("Battery:I2cBusPath must be a non-empty device path when present.");
            }
            else
            {
                busPath = rawBus.Trim();
            }
        }

        var address = 0;
        var rawAddress = Get(values, "I2cAddress");
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            errors.Add("Required Battery configuration value 'I2cAddress' is missing.");
        }
        else if (!TryParseAddress(rawAddress, out address))
        {
            errors.Add($"Battery:I2cAddress '{rawAddress}' is not a valid address (decimal or 0xNN).");
        }
        else if (address is < 0x03 or > 0x77)
        {
            errors.Add($"Battery:I2cAddress '0x{address:X2}' is outside the valid 7-bit range 0x03–0x77.");
        }

        var full = ParseVoltage(values, "FullVoltage", required: true, errors);
        if (full is not null && full <= 0)
        {
            errors.Add("Battery:FullVoltage must be greater than zero.");
        }

        var empty = ParseVoltage(values, "EmptyVoltage", required: true, errors);
        if (full is not null and > 0 && empty is not null && empty >= full)
        {
            errors.Add("Battery:EmptyVoltage must be lower than Battery:FullVoltage.");
        }

        var low = ParsePercent(values, "LowPercent", DefaultLowPercent, errors);
        var critical = ParsePercent(values, "CriticalPercent", DefaultCriticalPercent, errors);
        if (low is not null && critical is not null && critical > low)
        {
            errors.Add("Battery:CriticalPercent must be less than or equal to Battery:LowPercent.");
        }

        var shunt = ParseShuntOhms(values, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(values));
        }

        return new BatteryOptions(busPath, address, full!.Value, empty!.Value, shunt!.Value, low!.Value, critical!.Value);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static bool TryParseAddress(string raw, out int value)
    {
        var text = raw.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static double? ParseVoltage(
        IReadOnlyDictionary<string, string?> values,
        string key,
        bool required,
        List<string> errors)
    {
        var raw = Get(values, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (required)
            {
                errors.Add($"Required Battery configuration value '{key}' is missing.");
            }

            return null;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed < 0)
        {
            errors.Add($"Battery:{key} '{raw}' must be a non-negative number of volts.");
            return null;
        }

        return parsed;
    }

    private static int? ParsePercent(
        IReadOnlyDictionary<string, string?> values,
        string key,
        int fallback,
        List<string> errors)
    {
        var raw = Get(values, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 0 or > 100)
        {
            errors.Add($"Battery:{key} '{raw}' must be an integer between 0 and 100.");
            return null;
        }

        return parsed;
    }

    private static double? ParseShuntOhms(IReadOnlyDictionary<string, string?> values, List<string> errors)
    {
        var raw = Get(values, "ShuntOhms");
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add("Required Battery configuration value 'ShuntOhms' is missing.");
            return null;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed <= 0)
        {
            errors.Add($"Battery:ShuntOhms '{raw}' must be a positive number of ohms.");
            return null;
        }

        return parsed;
    }
}
