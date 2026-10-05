namespace DroneControl.Application;

/// <summary>
/// Software-level outcome of one hardware-layer operation
/// (see <see cref="CommandExecutionResult"/>). No value here asserts anything
/// about physical output.
/// </summary>
public enum HardwareCommandStatus
{
    /// <summary>
    /// A reachable hardware layer refused the operation for a logical reason.
    /// Default value: the pessimistic one — an unset outcome never reads as
    /// "applied".
    /// </summary>
    Rejected = 0,

    /// <summary>
    /// The operation reached the hardware layer and was executed at software
    /// level. This is NOT a claim that anything physical moved.
    /// </summary>
    Applied = 1,
}
