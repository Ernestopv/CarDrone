using System.Globalization;

namespace DroneControl.Infrastructure;

/// <summary>
/// Lowest GPIO output boundary for <c>HARDWARE_MODE=dry-run</c>
/// (specs/hardware/dry-run.md). It validates and records every intended
/// operation, then SUPPRESSES it: this type holds no reference to
/// <see cref="IRaspberryGpioPlatform"/>, <see cref="RaspberryGpioController"/>,
/// or any other physical sink, so a dry-run graph is structurally incapable of
/// a physical write — not merely switched off by a runtime flag.
///
/// A failure to record an intended operation propagates to the caller
/// (fail-closed); there is no real sink it could fall through to. Rejected or
/// unknown operations are recorded and then rejected with the same exception
/// contract as <see cref="MockGpioController"/>. No motor, command, L298N, or
/// electrical semantics exist here, and a record is never a confirmation.
/// </summary>
public sealed class DryRunGpioController : IGpioController
{
    /// <summary>Suppression reason for a valid intended output operation.</summary>
    public const string OutputSuppressedReason =
        "HARDWARE_MODE=dry-run: intended operation recorded at the dry-run output boundary and suppressed; no physical write is performed.";

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
    /// Creates the dry-run sink. The record callback is invoked BEFORE the
    /// operation is considered suppressed; if it throws, the operation fails
    /// closed and no state is committed.
    /// </summary>
    public DryRunGpioController(GpioPinConfiguration configuration, Action<DryRunOperationRecord> record)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _configuredIdentifiers = configuration.DigitalLineIdentifiers.ToHashSet();
    }

    public void ConfigureOutput(int pin)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_configuredIdentifiers.Contains(pin))
            {
                Record(
                    HardwareOutputCategory.Gpio,
                    pin.ToString(CultureInfo.InvariantCulture),
                    "configure-output",
                    RejectedOperationReason);
                throw new ArgumentException(
                    $"GPIO identifier {pin} is not present in the validated configuration.",
                    nameof(pin));
            }

            // Record first, then commit. No platform call exists to perform.
            Record(
                HardwareOutputCategory.Gpio,
                pin.ToString(CultureInfo.InvariantCulture),
                "configure-output",
                OutputSuppressedReason);
            _configuredOutputs.Add(pin);
        }
    }

    public void Write(int pin, GpioPinValue value)
    {
        if (!Enum.IsDefined(value))
        {
            Record(
                HardwareOutputCategory.Gpio,
                pin.ToString(CultureInfo.InvariantCulture),
                $"write value={Convert.ToInt32(value, CultureInfo.InvariantCulture)}",
                RejectedOperationReason);
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown GPIO digital value.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_configuredIdentifiers.Contains(pin))
            {
                Record(
                    HardwareOutputCategory.Gpio,
                    pin.ToString(CultureInfo.InvariantCulture),
                    $"write value={value}",
                    RejectedOperationReason);
                throw new ArgumentException(
                    $"GPIO identifier {pin} is not present in the validated configuration.",
                    nameof(pin));
            }

            if (!_configuredOutputs.Contains(pin))
            {
                Record(
                    HardwareOutputCategory.Gpio,
                    pin.ToString(CultureInfo.InvariantCulture),
                    $"write value={value}",
                    RejectedOperationReason);
                throw new InvalidOperationException(
                    $"GPIO identifier {pin} is not configured as an output.");
            }

            // Record first; suppression is the entire remainder of the method.
            Record(
                HardwareOutputCategory.Gpio,
                pin.ToString(CultureInfo.InvariantCulture),
                $"write value={value}",
                OutputSuppressedReason);
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
        HardwareOutputCategory category,
        string identifier,
        string requestedParameters,
        string suppressionReason)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        // Deliberately invoked (possibly throwing) BEFORE any state commit:
        // a record failure must fail the operation closed with nothing applied.
        _record(DryRunOperationRecord.Suppressed(
            category,
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
