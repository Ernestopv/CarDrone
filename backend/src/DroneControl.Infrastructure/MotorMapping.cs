using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Validated motor-mapping data (specs/hardware/motor-control.md): for each
/// semantic <see cref="DroneCommand"/> (including an explicit <c>stop</c>
/// entry) the OPERATOR-ASSERTED digital level of every configured identifier
/// (Pin1..Pin4), plus the <c>DirectionMappingVerified</c> operator assertion.
///
/// Safety properties:
/// - This type contains no mapping data of its own — levels are always
///   operator-supplied configuration; the shipped default configuration has
///   no mapping section at all.
/// - Instances exist only through <see cref="FromSection"/>, which enforces
///   the full validation ruleset and reports every violated rule at once,
///   so an instance is always complete (five commands, full identifier
///   coverage, HIGH/LOW tokens only).
/// - <see cref="DirectionMappingVerified"/> is an assertion, not physical
///   proof — the same stance as the Task 7 external-safety flag. Activation
///   happens only at the composition root (hardware-backed modes only).
///
/// Nothing above Infrastructure may reference this type except the
/// composition root.
/// </summary>
public sealed class MotorMapping
{
    private static readonly string[] AcceptedIdentifiers =
    [
        nameof(GpioPinConfiguration.Pin1),
        nameof(GpioPinConfiguration.Pin2),
        nameof(GpioPinConfiguration.Pin3),
        nameof(GpioPinConfiguration.Pin4),
    ];

    private readonly Dictionary<DroneCommand, Dictionary<int, GpioPinValue>> _commands;

    private MotorMapping(
        bool directionMappingVerified,
        IReadOnlyList<int> lineIdentifiers,
        Dictionary<DroneCommand, Dictionary<int, GpioPinValue>> commands)
    {
        DirectionMappingVerified = directionMappingVerified;
        LineIdentifiers = lineIdentifiers.ToArray();
        _commands = commands;
        Commands = commands.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<int, GpioPinValue>)pair.Value);
    }

    /// <summary>
    /// The operator assertion. <c>false</c> (the default) means the mapping is
    /// never used: the composition root keeps the provider inert.
    /// </summary>
    public bool DirectionMappingVerified { get; }

    /// <summary>
    /// The mapped line identifiers in the bound <see cref="GpioPinConfiguration"/>
    /// name order (Pin1..Pin4) — the deterministic operation order every
    /// command application follows.
    /// </summary>
    public IReadOnlyList<int> LineIdentifiers { get; }

    /// <summary>Asserted level per line identifier, for every command.</summary>
    public IReadOnlyDictionary<DroneCommand, IReadOnlyDictionary<int, GpioPinValue>> Commands { get; }

    /// <summary>
    /// Binds and validates one flat representation of the configuration
    /// section (keys such as <c>DirectionMappingVerified</c> and
    /// <c>Commands:forward:Pin1</c>; values <c>true/false</c> and
    /// <c>HIGH/LOW</c> — case-insensitive, trimmed). The caller decides
    /// whether the section exists; this method only validates its content.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Any validation problem — the message enumerates EVERY violated rule so
    /// the composition root can abort startup with the complete list.
    /// </exception>
    public static MotorMapping FromSection(
        IReadOnlyDictionary<string, string?> values,
        GpioPinConfiguration gpioConfiguration)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(gpioConfiguration);

        var errors = new List<string>();

        // Rule 1: the operator assertion (absent → false; malformed → error).
        // Looked up case-insensitively so an environment-variable source
        // (which preserves variable-name casing) behaves like every other
        // configuration provider.
        var verified = false;
        string? flagRaw = null;
        var flagPresent = false;
        foreach (var pair in values)
        {
            if (pair.Key.Equals("DirectionMappingVerified", StringComparison.OrdinalIgnoreCase))
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
                $"DirectionMappingVerified must be 'true' or 'false' (found '{flagRaw ?? "<empty>"}').");
        }

        var lineByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(GpioPinConfiguration.Pin1)] = gpioConfiguration.Pin1,
            [nameof(GpioPinConfiguration.Pin2)] = gpioConfiguration.Pin2,
            [nameof(GpioPinConfiguration.Pin3)] = gpioConfiguration.Pin3,
            [nameof(GpioPinConfiguration.Pin4)] = gpioConfiguration.Pin4,
        };
        var acceptedCommands = new Dictionary<string, DroneCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<DroneCommand>())
        {
            // Enum-derived so the command set can never drift from the Domain.
            acceptedCommands[name] = Enum.Parse<DroneCommand>(name);
        }

        // Commands that appeared at least once (even with broken entries), to
        // distinguish "missing command" from "incomplete coverage".
        var seen = new HashSet<DroneCommand>();
        var levels = new Dictionary<DroneCommand, Dictionary<int, GpioPinValue>>();
        var attempted = new Dictionary<DroneCommand, HashSet<string>>();

        foreach (var pair in values)
        {
            if (pair.Key.Equals("DirectionMappingVerified", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Key.Equals("Commands", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("MotorMapping 'Commands' is present but contains no command entries.");
                continue;
            }

            if (!pair.Key.StartsWith("Commands:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Unknown MotorMapping entry '{pair.Key}'.");
                continue;
            }

            var rest = pair.Key["Commands:".Length..];
            if (rest.Length == 0)
            {
                errors.Add("MotorMapping 'Commands' is present but contains no command entries.");
                continue;
            }

            var separator = rest.IndexOf(':');
            var commandToken = separator < 0 ? rest : rest[..separator];
            var identifierToken = separator < 0 ? null : rest[(separator + 1)..];

            if (!acceptedCommands.TryGetValue(commandToken, out var command))
            {
                errors.Add(
                    $"Unknown motor mapping command '{commandToken}'; accepted commands: " +
                    $"{string.Join(", ", acceptedCommands.Keys.Select(key => key.ToLowerInvariant()))}.");
                continue;
            }

            seen.Add(command);
            if (!attempted.TryGetValue(command, out var attemptedIdentifiers))
            {
                attemptedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                attempted[command] = attemptedIdentifiers;
                levels[command] = [];
            }

            if (identifierToken is null)
            {
                // Present but empty entry: presence is recorded, coverage
                // errors below name every missing identifier.
                continue;
            }

            if (!lineByName.TryGetValue(identifierToken, out var lineIdentifier))
            {
                errors.Add(
                    $"Motor mapping identifier '{identifierToken}' is not accepted; only the configured " +
                    $"digital identifiers {string.Join(", ", AcceptedIdentifiers)} are allowed " +
                    "(PWM identifiers are accepted only through the PwmMapping section).");
                continue;
            }

            attemptedIdentifiers.Add(identifierToken);

            if (pair.Value is null || !TryParseLevel(pair.Value, out var level))
            {
                errors.Add(
                    $"Motor mapping level for 'Commands:{commandToken}:{identifierToken}' must be HIGH or LOW " +
                    $"(found '{pair.Value ?? "<empty>"}').");
                continue;
            }

            if (!levels[command].TryAdd(lineIdentifier, level))
            {
                errors.Add(
                    $"Motor mapping has a duplicate entry for command '{commandToken}' and identifier " +
                    $"'{identifierToken}'.");
            }
        }

        // Rule 2 + 3: every command present, every configured identifier covered.
        foreach (var name in Enum.GetNames<DroneCommand>())
        {
            var command = Enum.Parse<DroneCommand>(name);
            if (!seen.Contains(command))
            {
                errors.Add(
                    $"Motor mapping command '{name.ToLowerInvariant()}' is missing; all " +
                    $"{acceptedCommands.Count} commands are required.");
                continue;
            }

            foreach (var identifier in AcceptedIdentifiers)
            {
                if (!attempted[command].Contains(identifier))
                {
                    errors.Add(
                        $"Motor mapping command '{name.ToLowerInvariant()}' is missing a level for " +
                        $"identifier '{identifier}'.");
                }
            }
        }

        if (verified && seen.Count == 0)
        {
            // Matrix row "absent | true": an assertion without mapping data is
            // a contradiction, never a silent inert fallback.
            errors.Add(
                "DirectionMappingVerified=true asserts a verified mapping, but no Commands mapping data " +
                "is present.");
        }

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"MotorMapping validation failed ({errors.Count} problem(s)): {string.Join(" ", errors)}",
                nameof(values));
        }

        return new MotorMapping(verified, gpioConfiguration.DigitalLineIdentifiers, levels);
    }

    private static bool TryParseLevel(string raw, out GpioPinValue level)
    {
        var token = raw.Trim();
        if (token.Equals("HIGH", StringComparison.OrdinalIgnoreCase))
        {
            level = GpioPinValue.High;
            return true;
        }

        if (token.Equals("LOW", StringComparison.OrdinalIgnoreCase))
        {
            level = GpioPinValue.Low;
            return true;
        }

        level = default;
        return false;
    }
}
