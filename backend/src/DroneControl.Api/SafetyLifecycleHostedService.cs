using DroneControl.Infrastructure;

namespace DroneControl.Api;

/// <summary>
/// Adapts ASP.NET Core host lifetime notifications to the Infrastructure
/// safety coordinator. The coordinator owns the state machine; this class only
/// invokes its startup/shutdown hooks and introduces no GPIO path.
/// </summary>
public sealed class SafetyLifecycleHostedService(
    DroneSafetyController safetyController) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => safetyController.StartSafetyAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => safetyController.StopSafetyAsync(cancellationToken);
}
