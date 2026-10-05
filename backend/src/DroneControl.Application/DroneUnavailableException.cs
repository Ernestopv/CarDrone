namespace DroneControl.Application;

/// <summary>
/// Thrown when the drone implementation itself cannot service a request.
/// Reserved detection contract for the future hardware adapter: the Phase 2
/// simulator never throws it (no failure injection, Task 11), but the API
/// layer already maps it to 503 so the availability channel exists before
/// hardware work begins (specs/backend/error-handling.md).
/// </summary>
public sealed class DroneUnavailableException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public DroneUnavailableException()
        : base("The drone implementation is unavailable.")
    {
    }

    /// <summary>
    /// Creates the exception with a specific unavailability reason (surfaced
    /// as the 503 ProblemDetails detail so operators see WHY the existing
    /// availability channel fired). Same type, same detection contract.
    /// </summary>
    public DroneUnavailableException(string message)
        : base(message)
    {
    }
}
