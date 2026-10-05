namespace DroneControl.Infrastructure;

/// <summary>
/// Applies a validated <see cref="PwmMapping"/> to the configured PWM
/// identifiers through <see cref="IPwmController"/>
/// (specs/hardware/pwm.md).
///
/// Mode-agnostic by design: this type never reads the hardware mode. Whether
/// the issued operations reach a physical sink or the suppressing dry-run
/// boundary is decided solely at the composition root through the injected
/// <see cref="IPwmController"/> — the same upstream flow in every
/// hardware-backed mode.
///
/// Tracking (Infrastructure-only): <see cref="LastRequestedSpeedPercent"/>
/// (the 0-100 application speed) and <see cref="LastAppliedDutyPercent"/>
/// (the duty percent actually issued) are updated only after the complete
/// operation sequence succeeds; a failure leaves both at their previous
/// values. Neither is exposed on the wire.
///
/// Honesty scope: a completed <see cref="Apply"/> means the intended duty
/// operations were issued in deterministic order — it is never a claim that a
/// motor spun, that duty 0 stops anything, or that hardware confirmed
/// anything, and the envelope values themselves are operator assertions, not
/// verified electrical truth. Operations are sequential, not atomic: a
/// mid-sequence failure can leave earlier operations issued; there is no
/// retry here (Task 26 owns STOP/retry policy).
/// </summary>
public sealed class SpeedController : IDisposable
{
    private readonly object _gate = new();
    private readonly IPwmController _pwm;
    private readonly PwmMapping _mapping;
    private int? _lastRequestedSpeedPercent;
    private int? _lastAppliedDutyPercent;
    private bool _disposed;

    /// <summary>Creates the controller over the output sink and a validated envelope.</summary>
    public SpeedController(IPwmController pwm, PwmMapping mapping)
    {
        _pwm = pwm ?? throw new ArgumentNullException(nameof(pwm));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
    }

    /// <summary>
    /// The last requested application speed (0-100), or <c>null</c> until the
    /// first complete success. This is the backlog's <c>requestedSpeed</c>.
    /// </summary>
    public int? LastRequestedSpeedPercent
    {
        get
        {
            lock (_gate)
            {
                return _lastRequestedSpeedPercent;
            }
        }
    }

    /// <summary>
    /// The last duty percent actually issued to the sink, or <c>null</c> until
    /// the first complete success. This is the backlog's <c>appliedSpeed</c>,
    /// expressed in the units the PWM output received.
    /// </summary>
    public int? LastAppliedDutyPercent
    {
        get
        {
            lock (_gate)
            {
                return _lastAppliedDutyPercent;
            }
        }
    }

    /// <summary>
    /// Applies the speed. Range check first — an illegal speed stays illegal
    /// in every mode and activation configuration (reject, never clamp).
    /// Deterministic order: every identifier is (re)configured with the
    /// asserted frequency first, then the computed duty is written in
    /// <see cref="PwmMapping.Identifiers"/> order (PWM1, then PWM2). Speed 0
    /// is a real application (duty 0 on both identifiers), never a skipped
    /// call. Tracking updates only after all four operations complete.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Speed outside 0-100.</exception>
    /// <exception cref="OperationCanceledException">Cancelled before any sink operation.</exception>
    public void Apply(int speedPercent, CancellationToken cancellationToken = default)
    {
        if (speedPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedPercent),
                speedPercent,
                "Speed must be between 0 and 100.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Resolve the duty BEFORE any output operation so no partial
            // application can stem from the mapping itself.
            var dutyPercent = ComputeDutyPercent(speedPercent);
            var identifiers = _mapping.Identifiers;

            for (var index = 0; index < identifiers.Count; index++)
            {
                _pwm.ConfigureOutput(identifiers[index], _mapping.FrequencyHz);
            }

            for (var index = 0; index < identifiers.Count; index++)
            {
                _pwm.SetDutyCycle(identifiers[index], dutyPercent);
            }

            _lastRequestedSpeedPercent = speedPercent;
            _lastAppliedDutyPercent = dutyPercent;
        }
    }

    /// <summary>
    /// Shutdown behavior (specs/hardware/pwm.md): best-effort duty 0 through
    /// the same deterministic sequence, issued if and only if a non-zero duty
    /// was previously applied. Failures propagate out of
    /// <see cref="Dispose"/> — never swallowed into a false success and never
    /// claimed as physical. Idempotent: a second disposal issues nothing.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Mark disposed before driving so use-after-dispose is
            // deterministic even if the shutdown sequence itself throws.
            _disposed = true;

            if (_lastAppliedDutyPercent is null or 0)
            {
                // Nothing non-zero was ever applied — nothing to idle.
                return;
            }

            var identifiers = _mapping.Identifiers;
            for (var index = 0; index < identifiers.Count; index++)
            {
                _pwm.ConfigureOutput(identifiers[index], _mapping.FrequencyHz);
            }

            for (var index = 0; index < identifiers.Count; index++)
            {
                _pwm.SetDutyCycle(identifiers[index], 0);
            }
        }
    }

    /// <summary>
    /// The task-defined speed → duty mapping (no electrical claim):
    /// speed 0 → duty 0 always; otherwise
    /// <c>Min + ((Max - Min) * speed) / 100</c> in integer arithmetic,
    /// truncated toward zero. Callers validate the range first.
    /// </summary>
    private int ComputeDutyPercent(int speedPercent)
    {
        if (speedPercent == 0)
        {
            return 0;
        }

        return _mapping.MinDutyPercent
            + ((_mapping.MaxDutyPercent - _mapping.MinDutyPercent) * speedPercent) / 100;
    }
}
