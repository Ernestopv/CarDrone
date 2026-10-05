using System.Globalization;

namespace DroneControl.Infrastructure;

/// <summary>
/// Lowest PWM output boundary for <c>HARDWARE_MODE=dry-run</c>
/// (specs/hardware/pwm.md, specs/hardware/dry-run.md). It validates and
/// records every intended PWM operation, then SUPPRESSES it: this type holds
/// no reference to <see cref="IRaspberryPwmPlatform"/>,
/// <see cref="RaspberryPwmController"/>, or any other physical sink, so a
/// dry-run graph is structurally incapable of a physical output change —
/// not merely switched off by a runtime flag.
///
/// A failure to record an intended operation propagates to the caller
/// (fail-closed); there is no real sink it could fall through to. Rejected or
/// unknown operations are recorded and then rejected with the same exception
/// contract as <see cref="DryRunGpioController"/>. No speed, motor, L298N,
/// frequency, or electrical semantics exist here (frequency and duty are
/// opaque validated parameters), and a record is never a confirmation.
/// </summary>
public sealed class DryRunPwmController : IPwmController
{
    /// <summary>Suppression reason for a valid intended output operation.</summary>
    public const string OutputSuppressedReason =
        "HARDWARE_MODE=dry-run: intended operation recorded at the dry-run output boundary and suppressed; no physical output is performed.";

    /// <summary>Suppression reason for a rejected/unknown output operation.</summary>
    public const string RejectedOperationReason =
        "Operation rejected by dry-run output validation; never forwarded to any output sink.";

    private readonly object _gate = new();
    private readonly HashSet<int> _configuredIdentifiers;
    private readonly HashSet<int> _configuredOutputs = [];
    private readonly Action<DryRunOperationRecord> _record;
    private long _sequence;
    private bool _disposed;

    /// <summary>
    /// Creates the dry-run PWM sink over the supplied validated identifiers.
    /// The record callback is invoked BEFORE the operation is considered
    /// suppressed; if it throws, the operation fails closed and no state is
    /// committed.
    /// </summary>
    public DryRunPwmController(
        IReadOnlyCollection<int> configuredIdentifiers,
        Action<DryRunOperationRecord> record)
    {
        ArgumentNullException.ThrowIfNull(configuredIdentifiers);
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _configuredIdentifiers = configuredIdentifiers.ToHashSet();
    }

    public void ConfigureOutput(int identifier, int frequencyHz)
    {
        var identifierText = identifier.ToString(CultureInfo.InvariantCulture);
        var parameters = string.Create(
            CultureInfo.InvariantCulture,
            $"configure frequency-hz={frequencyHz}");

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_configuredIdentifiers.Contains(identifier))
            {
                Record(identifierText, parameters, RejectedOperationReason);
                throw new ArgumentException(
                    $"PWM identifier {identifier} is not present in the validated configuration.",
                    nameof(identifier));
            }

            if (frequencyHz <= 0)
            {
                Record(identifierText, parameters, RejectedOperationReason);
                throw new ArgumentOutOfRangeException(
                    nameof(frequencyHz),
                    frequencyHz,
                    "PWM frequency must be a positive integer.");
            }

            // Record first, then commit. No platform call exists to perform.
            Record(identifierText, parameters, OutputSuppressedReason);
            _configuredOutputs.Add(identifier);
        }
    }

    public void SetDutyCycle(int identifier, int dutyPercent)
    {
        var identifierText = identifier.ToString(CultureInfo.InvariantCulture);
        var parameters = string.Create(
            CultureInfo.InvariantCulture,
            $"write duty-percent={dutyPercent}");

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_configuredIdentifiers.Contains(identifier))
            {
                Record(identifierText, parameters, RejectedOperationReason);
                throw new ArgumentException(
                    $"PWM identifier {identifier} is not present in the validated configuration.",
                    nameof(identifier));
            }

            if (dutyPercent is < 0 or > 100)
            {
                Record(identifierText, parameters, RejectedOperationReason);
                throw new ArgumentOutOfRangeException(
                    nameof(dutyPercent),
                    dutyPercent,
                    "PWM duty percent must be between 0 and 100.");
            }

            if (!_configuredOutputs.Contains(identifier))
            {
                Record(identifierText, parameters, RejectedOperationReason);
                throw new InvalidOperationException(
                    $"PWM identifier {identifier} is not configured as an output.");
            }

            // Record first; suppression is the entire remainder of the method.
            Record(identifierText, parameters, OutputSuppressedReason);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void Record(
        string identifier,
        string requestedParameters,
        string suppressionReason)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        // Deliberately invoked (possibly throwing) BEFORE any state commit:
        // a record failure must fail the operation closed with nothing applied.
        _record(DryRunOperationRecord.Suppressed(
            HardwareOutputCategory.Pwm,
            identifier,
            requestedParameters,
            sequence,
            DateTimeOffset.UtcNow,
            suppressionReason));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
