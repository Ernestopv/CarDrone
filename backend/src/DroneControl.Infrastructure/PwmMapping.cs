using System.Globalization;

namespace DroneControl.Infrastructure;

/// <summary>
/// Validated PWM envelope data (specs/hardware/pwm.md): the OPERATOR-SUPPLIED
/// frequency and duty bounds that map application speed (0-100) to a duty
/// cycle, the configured PWM identifiers from the GPIO section, and the
/// <c>SpeedMappingVerified</c> operator assertion.
///
/// Safety properties:
/// - This type contains no electrical values of its own — frequency and duty
///   bounds are always operator-supplied configuration; the shipped default
///   configuration has no PwmMapping section at all. No default frequency,
///   duty bound, or electrical limit is invented anywhere.
/// - Instances exist only through <see cref="FromSection"/>, which enforces
///   the full validation ruleset and reports every violated rule at once
///   (including PWM identifier problems), and returns <c>null</c> when the
///   section is valid but unasserted ("validated but unused").
/// - <see cref="SpeedMappingVerified"/> is an assertion, not physical proof —
///   the same stance as <c>DirectionMappingVerified</c> (Task 29) and the
///   Task 7 external-safety flag. Activation happens only at the composition
///   root (hardware-backed modes only).
///
/// Nothing above Infrastructure may reference this type except the
/// composition root.
/// </summary>
public sealed class PwmMapping
{
    private static readonly string[] AcceptedKeys =
    [
        "FrequencyHz",
        "MinDutyPercent",
        "MaxDutyPercent",
        "SpeedMappingVerified",
    ];

    private static readonly string[] PwmIdentifierKeys = ["PWM1", "PWM2"];

    private PwmMapping(
        int frequencyHz,
        int minDutyPercent,
        int maxDutyPercent,
        IReadOnlyList<int> identifiers,
        bool speedMappingVerified)
    {
        FrequencyHz = frequencyHz;
        MinDutyPercent = minDutyPercent;
        MaxDutyPercent = maxDutyPercent;
        Identifiers = identifiers;
        SpeedMappingVerified = speedMappingVerified;
    }

    /// <summary>
    /// Operator-supplied PWM frequency in Hz (integer &gt; 0). Software asserts
    /// no electrical upper bound — that is an operator/electrical concern.
    /// </summary>
    public int FrequencyHz { get; }

    /// <summary>
    /// Minimum usable duty percent for non-zero speeds (0-100),
    /// operator-supplied.
    /// </summary>
    public int MinDutyPercent { get; }

    /// <summary>Maximum duty percent (0-100, &gt;= Min), operator-supplied.</summary>
    public int MaxDutyPercent { get; }

    /// <summary>
    /// The configured PWM identifiers in <c>PWM1</c>, <c>PWM2</c> name order —
    /// the deterministic operation order every speed application follows.
    /// </summary>
    public IReadOnlyList<int> Identifiers { get; }

    /// <summary>
    /// The operator assertion. <c>false</c> (the default) means the mapping is
    /// never used: the composition root registers nothing PWM-related.
    /// </summary>
    public bool SpeedMappingVerified { get; }

    /// <summary>
    /// Binds and validates the flat representation of the
    /// <c>PwmMapping</c> configuration section together with the PWM
    /// identifiers (<c>PWM1</c>/<c>PWM2</c>) of the existing GPIO section.
    /// The caller decides whether the section exists; this method only
    /// validates its content. All content is validated regardless of the
    /// flag: an invalid section aborts startup even when unasserted.
    /// </summary>
    /// <returns>
    /// The validated mapping when <c>SpeedMappingVerified=true</c>;
    /// <c>null</c> when the section is valid but unasserted (validated but
    /// unused — the composition root registers nothing).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Any validation problem — the message enumerates EVERY violated rule
    /// (section content and PWM identifier problems together) so the
    /// composition root can abort startup with the complete list.
    /// </exception>
    public static PwmMapping? FromSection(
        IReadOnlyDictionary<string, string?> values,
        GpioPinConfiguration gpioConfiguration,
        IReadOnlyDictionary<string, string?> gpioSectionValues)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(gpioConfiguration);
        ArgumentNullException.ThrowIfNull(gpioSectionValues);

        var errors = new List<string>();

        // Rule 1: the operator assertion (absent → false; malformed → error).
        // Case-insensitive so an environment-variable source behaves like any
        // other configuration provider (same rule as MotorMapping).
        var verified = false;
        string? flagRaw = null;
        var flagPresent = false;
        foreach (var pair in values)
        {
            if (pair.Key.Equals("SpeedMappingVerified", StringComparison.OrdinalIgnoreCase))
            {
                flagPresent = true;
                flagRaw = pair.Value;
                break;
            }
        }

        if (flagPresent
            && (flagRaw is null || !bool.TryParse(flagRaw.Trim(), out verified)))
        {
            verified = false;
            errors.Add(
                $"SpeedMappingVerified must be 'true' or 'false' (found '{flagRaw ?? "<empty>"}').");
        }

        // Rule 2-4: envelope values. Required whenever the section exists —
        // no default frequency or duty bound is ever supplied by software.
        var frequency = ParsePositiveInteger(values, "FrequencyHz", errors);
        var minDuty = ParseDutyPercent(values, "MinDutyPercent", errors);
        var maxDuty = ParseDutyPercent(values, "MaxDutyPercent", errors);
        if (minDuty is >= 0 && maxDuty is >= 0 && maxDuty < minDuty)
        {
            errors.Add("MaxDutyPercent must be greater than or equal to MinDutyPercent.");
        }

        // Rule 5: unknown keys are errors (same rule as MotorMapping).
        foreach (var pair in values)
        {
            if (!AcceptedKeys.Any(key => key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Unknown PwmMapping entry '{pair.Key}'.");
            }
        }

        // Rule 6: the PWM identifiers of the existing GPIO section — present,
        // positive integers, distinct from each other and from Pin1-Pin4
        // (the duplicate-identifier rule of specs/hardware/gpio.md).
        var pwm1 = ParsePwmIdentifier(gpioSectionValues, "PWM1", errors);
        var pwm2 = ParsePwmIdentifier(gpioSectionValues, "PWM2", errors);
        if (pwm1.HasValue && pwm2.HasValue)
        {
            if (pwm1.Value == pwm2.Value)
            {
                errors.Add(
                    $"GPIO:PWM1 and GPIO:PWM2 must be distinct identifiers (both '{pwm1.Value}').");
            }
            else
            {
                var digital = gpioConfiguration.DigitalLineIdentifiers.ToHashSet();
                if (digital.Contains(pwm1.Value))
                {
                    errors.Add(
                        $"GPIO:PWM1 must not collide with the digital identifiers Pin1-Pin4 " +
                        $"(found '{pwm1.Value}').");
                }

                if (digital.Contains(pwm2.Value))
                {
                    errors.Add(
                        $"GPIO:PWM2 must not collide with the digital identifiers Pin1-Pin4 " +
                        $"(found '{pwm2.Value}').");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"PwmMapping validation failed ({errors.Count} problem(s)): {string.Join(" ", errors)}",
                nameof(values));
        }

        if (!verified)
        {
            // Matrix row "present, valid | false": validated but unused.
            return null;
        }

        // Defensive completeness check (unreachable: every null parse path
        // above adds an error, and errors throw before this point). Explicitly
        // narrowed instead of silently defaulting to a value — a validation
        // bug must abort, never ship duty 0 by accident.
        if (frequency is not { } frequencyHz
            || minDuty is not { } minDutyPercent
            || maxDuty is not { } maxDutyPercent
            || pwm1 is not { } pwm1Identifier
            || pwm2 is not { } pwm2Identifier)
        {
            throw new InvalidOperationException(
                "PwmMapping validation produced an incomplete result.");
        }

        return new PwmMapping(
            frequencyHz,
            minDutyPercent,
            maxDutyPercent,
            [pwm1Identifier, pwm2Identifier],
            speedMappingVerified: true);
    }

    private static int? ParsePositiveInteger(
        IReadOnlyDictionary<string, string?> values,
        string key,
        List<string> errors)
    {
        if (!TryGet(values, key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            errors.Add($"{key} must be a positive integer (found '<empty>').");
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            errors.Add($"{key} must be a positive integer (found '{raw}').");
            return null;
        }

        return parsed;
    }

    private static int? ParseDutyPercent(
        IReadOnlyDictionary<string, string?> values,
        string key,
        List<string> errors)
    {
        if (!TryGet(values, key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            errors.Add($"{key} must be an integer between 0 and 100 (found '<empty>').");
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 0 or > 100)
        {
            errors.Add($"{key} must be an integer between 0 and 100 (found '{raw}').");
            return null;
        }

        return parsed;
    }

    private static int? ParsePwmIdentifier(
        IReadOnlyDictionary<string, string?> gpioValues,
        string key,
        List<string> errors)
    {
        if (!TryGet(gpioValues, key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            errors.Add($"GPIO:{key} must be a positive integer (found '<empty>').");
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            errors.Add($"GPIO:{key} must be a positive integer (found '{raw}').");
            return null;
        }

        return parsed;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string?> values, string key, out string? raw)
    {
        foreach (var pair in values)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                raw = pair.Value;
                return true;
            }
        }

        raw = null;
        return false;
    }
}
