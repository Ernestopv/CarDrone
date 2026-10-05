namespace DroneControl.Infrastructure;

/// <summary>
/// Output operation category vocabulary for dry-run records
/// (specs/hardware/dry-run.md, specs/hardware/pwm.md). <see cref="Gpio"/>
/// covers digital line operations (Task 25 seam); <see cref="Pwm"/> covers
/// PWM output-boundary operations produced by <see cref="DryRunPwmController"/>
/// (Task 30).
/// </summary>
public enum HardwareOutputCategory
{
    /// <summary>A digital GPIO line operation (Task 25 seam).</summary>
    Gpio,

    /// <summary>A PWM output-boundary operation (Task 30 seam).</summary>
    Pwm,
}

/// <summary>
/// Structured record of an INTENDED hardware output operation that dry-run
/// suppressed. Intent only: a record is never a confirmation, acknowledgement,
/// or claim of physical actuation. Invariants are enforced by
/// <see cref="Suppressed"/> — every record carries <c>DryRun=true</c> and
/// <c>PhysicallyApplied=false</c>.
/// </summary>
public sealed record DryRunOperationRecord
{
    private DryRunOperationRecord(
        HardwareOutputCategory category,
        string identifier,
        string requestedParameters,
        long sequence,
        DateTimeOffset timestampUtc,
        bool dryRun,
        bool physicallyApplied,
        string suppressionReason)
    {
        Category = category;
        Identifier = identifier;
        RequestedParameters = requestedParameters;
        Sequence = sequence;
        TimestampUtc = timestampUtc;
        DryRun = dryRun;
        PhysicallyApplied = physicallyApplied;
        SuppressionReason = suppressionReason;
    }

    /// <summary>Operation category (GPIO or PWM).</summary>
    public HardwareOutputCategory Category { get; }

    /// <summary>
    /// Configured identifier/reference supplied by the hardware path, without
    /// any added motor or command meaning.
    /// </summary>
    public string Identifier { get; }

    /// <summary>Requested value/parameters exactly as supplied by the caller.</summary>
    public string RequestedParameters { get; }

    /// <summary>Monotonic sequence number sufficient to verify ordering.</summary>
    public long Sequence { get; }

    /// <summary>UTC timestamp of the intended operation.</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>Always true for dry-run records.</summary>
    public bool DryRun { get; }

    /// <summary>Always false for dry-run records: nothing physical happened.</summary>
    public bool PhysicallyApplied { get; }

    /// <summary>Why the operation was suppressed (or rejected) instead of applied.</summary>
    public string SuppressionReason { get; }

    /// <summary>
    /// Creates a suppressed-operation record. The invariants
    /// <c>DryRun=true</c> and <c>PhysicallyApplied=false</c> are structural:
    /// no factory argument can turn a record into a physical claim.
    /// </summary>
    public static DryRunOperationRecord Suppressed(
        HardwareOutputCategory category,
        string identifier,
        string requestedParameters,
        long sequence,
        DateTimeOffset timestampUtc,
        string suppressionReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedParameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(suppressionReason);

        return new DryRunOperationRecord(
            category,
            identifier,
            requestedParameters,
            sequence,
            timestampUtc,
            dryRun: true,
            physicallyApplied: false,
            suppressionReason);
    }
}
