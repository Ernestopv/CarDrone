namespace DroneControl.Infrastructure;

/// <summary>
/// Validated software control-path deadlines and retry bounds. These values
/// are not electrical or motor-safety limits.
/// </summary>
public sealed class DroneSafetyOptions
{
    public const int MaximumCommandTimeoutMilliseconds = 30_000;
    public const int MaximumStopTimeoutMilliseconds = 5_000;
    public const int MaximumRetryCount = 3;
    public const int MaximumRetryDelayMilliseconds = 2_000;

    public DroneSafetyOptions(
        int commandTimeoutMilliseconds,
        int stopTimeoutMilliseconds,
        int stopRetryCount,
        int stopRetryDelayMilliseconds,
        int recoveryRetryCount,
        int recoveryRetryDelayMilliseconds,
        bool realHardwareMode = false,
        bool externalAbruptFailureProtectionVerified = false)
    {
        ValidateRange(commandTimeoutMilliseconds, 1, MaximumCommandTimeoutMilliseconds, nameof(commandTimeoutMilliseconds));
        ValidateRange(stopTimeoutMilliseconds, 1, MaximumStopTimeoutMilliseconds, nameof(stopTimeoutMilliseconds));
        ValidateRange(stopRetryCount, 0, MaximumRetryCount, nameof(stopRetryCount));
        ValidateRange(stopRetryDelayMilliseconds, 0, MaximumRetryDelayMilliseconds, nameof(stopRetryDelayMilliseconds));
        ValidateRange(recoveryRetryCount, 0, MaximumRetryCount, nameof(recoveryRetryCount));
        ValidateRange(recoveryRetryDelayMilliseconds, 0, MaximumRetryDelayMilliseconds, nameof(recoveryRetryDelayMilliseconds));

        CommandTimeout = TimeSpan.FromMilliseconds(commandTimeoutMilliseconds);
        StopTimeout = TimeSpan.FromMilliseconds(stopTimeoutMilliseconds);
        StopRetryCount = stopRetryCount;
        StopRetryDelay = TimeSpan.FromMilliseconds(stopRetryDelayMilliseconds);
        RecoveryRetryCount = recoveryRetryCount;
        RecoveryRetryDelay = TimeSpan.FromMilliseconds(recoveryRetryDelayMilliseconds);
        RealHardwareMode = realHardwareMode;
        ExternalAbruptFailureProtectionVerified = externalAbruptFailureProtectionVerified;
    }

    public TimeSpan CommandTimeout { get; }
    public TimeSpan StopTimeout { get; }
    /// <summary>Additional attempts after the initial STOP request.</summary>
    public int StopRetryCount { get; }
    public TimeSpan StopRetryDelay { get; }
    /// <summary>Additional safe-baseline attempts after the initial attempt.</summary>
    public int RecoveryRetryCount { get; }
    public TimeSpan RecoveryRetryDelay { get; }
    /// <summary>Whether composition selected HARDWARE_MODE=real.</summary>
    public bool RealHardwareMode { get; }
    /// <summary>
    /// Explicit operator assertion that the external protection for abrupt
    /// process/power failure was separately verified. Defaults false and must
    /// not be enabled without evidence.
    /// </summary>
    public bool ExternalAbruptFailureProtectionVerified { get; }

    private static void ValidateRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, value,
                $"Safety setting must be between {minimum} and {maximum} (inclusive).");
        }
    }
}
