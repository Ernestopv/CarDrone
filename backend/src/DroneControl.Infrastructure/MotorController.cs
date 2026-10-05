using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Applies a validated <see cref="MotorMapping"/> to the configured digital
/// outputs through <see cref="IGpioController"/>
/// (specs/hardware/motor-control.md).
///
/// Mode-agnostic by design: this type never reads the hardware mode. Whether
/// the issued operations reach a physical sink or the suppressing dry-run
/// boundary is decided solely at the composition root through the injected
/// <see cref="IGpioController"/> — the same upstream flow in every
/// hardware-backed mode (specs/hardware/dry-run.md Interfaces rule).
///
/// Honesty scope: a completed <see cref="Apply"/> means the operator-asserted
/// output operations were issued in deterministic order — it is never a claim
/// that a motor moved or that hardware confirmed anything, and the mapping
/// levels themselves are operator assertions, not verified physical truth.
/// Operations are sequential, not atomic: a mid-sequence failure can leave
/// earlier lines written; there is no retry here (Task 26 owns STOP/retry
/// policy).
/// </summary>
public sealed class MotorController
{
    private readonly IGpioController _gpio;
    private readonly MotorMapping _mapping;

    /// <summary>Creates the controller over the output sink and a validated mapping.</summary>
    public MotorController(IGpioController gpio, MotorMapping mapping)
    {
        _gpio = gpio ?? throw new ArgumentNullException(nameof(gpio));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
    }

    /// <summary>
    /// Applies the asserted levels for the semantic command. Deterministic
    /// order: every mapped line is (re)configured as an output first, then the
    /// levels are written in <see cref="MotorMapping.LineIdentifiers"/> order
    /// (the Pin1..Pin4 configuration-name order). The <c>stop</c> entry is
    /// applied like any other command — no special casing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Undefined command value.</exception>
    /// <exception cref="InvalidOperationException">
    /// The mapping has no entry for the command (unreachable through
    /// <see cref="MotorMapping.FromSection"/>, which requires all commands) —
    /// fails closed before touching the sink, never a partial application.
    /// </exception>
    public void Apply(DroneCommand command)
    {
        if (!Enum.IsDefined(command))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown drone command.");
        }

        if (!_mapping.Commands.TryGetValue(command, out var levels))
        {
            // Defensive fail-closed: no sink operation happens without a
            // complete asserted entry.
            throw new InvalidOperationException(
                $"The motor mapping has no entry for command '{command}'.");
        }

        // Resolve every level BEFORE any output operation so an inconsistent
        // mapping can never cause a partial application.
        var identifiers = _mapping.LineIdentifiers;
        var values = new GpioPinValue[identifiers.Count];
        for (var index = 0; index < identifiers.Count; index++)
        {
            if (!levels.TryGetValue(identifiers[index], out values[index]))
            {
                throw new InvalidOperationException(
                    $"The motor mapping entry for command '{command}' has no level for line " +
                    $"'{identifiers[index]}'.");
            }
        }

        for (var index = 0; index < identifiers.Count; index++)
        {
            _gpio.ConfigureOutput(identifiers[index]);
        }

        for (var index = 0; index < identifiers.Count; index++)
        {
            _gpio.Write(identifiers[index], values[index]);
        }
    }
}
