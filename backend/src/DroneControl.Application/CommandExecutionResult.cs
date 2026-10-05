namespace DroneControl.Application;

/// <summary>
/// Outcome of a single hardware-layer operation. Software-level semantics
/// only: <see cref="HardwareCommandStatus.Applied"/> means the operation
/// reached and executed in the hardware layer — it is never a physical
/// actuation confirmation, and no future member of this type may imply one
/// (hardware confirmation requires a separate, verified contract — see the
/// prerequisites ledger in specs/architecture/runtime-deployment.md).
/// </summary>
/// <param name="Status">The layered outcome.</param>
/// <param name="Reason">
/// Optional human-readable explanation, intended for logical refusals.
/// An unreachable layer must throw <see cref="DroneUnavailableException"/>
/// instead of reporting a rejection.
/// </param>
public sealed record CommandExecutionResult(
    HardwareCommandStatus Status,
    string? Reason = null);
