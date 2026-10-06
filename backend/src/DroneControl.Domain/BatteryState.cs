namespace DroneControl.Domain;

/// <summary>
/// Battery level state. Zero-valued <see cref="Unknown"/> is the pessimistic
/// default: an unset or idle status never reads as a healthy level.
/// </summary>
public enum BatteryState
{
    /// <summary>No valid state is known (default; also an unconfigured window).</summary>
    Unknown = 0,

    /// <summary>Reading is above the low threshold.</summary>
    Ok,

    /// <summary>Reading is at or below the low threshold.</summary>
    Low,

    /// <summary>Reading is at or below the critical threshold.</summary>
    Critical,

    /// <summary>The sensor was not reachable / the read failed.</summary>
    Error,
}
