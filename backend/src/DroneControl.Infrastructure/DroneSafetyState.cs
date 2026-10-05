namespace DroneControl.Infrastructure;

/// <summary>Internal software safety state; never a statement about physical motor state.</summary>
public enum DroneSafetyState
{
    Unverified,
    Safe,
    CommandActive,
    StopPending,
    Faulted,
    Recovering,
}

/// <summary>Observed event category for structured safety logs.</summary>
public enum DroneSafetyReason
{
    Startup,
    MovementCommand,
    StopRequested,
    CommandTimeout,
    HardwareException,
    HardwareUnavailable,
    ConnectionLoss,
    GracefulShutdown,
    RecoveryFailure,
    InvalidOrRejectedOperation,
}

/// <summary>Structured software-level safety event; contains no physical claim.</summary>
public sealed record DroneSafetyEvent(
    DroneSafetyReason Reason,
    DroneSafetyState State,
    DateTimeOffset Timestamp,
    string Message,
    string? ExceptionType = null);
