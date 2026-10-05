using DroneControl.Infrastructure;

namespace DroneControl.Api;

/// <summary>
/// Adapts ASP.NET Core host lifetime notifications to the Infrastructure
/// camera monitor (specs/hardware/camera-runtime.md). The monitor owns the
/// probe loop; this class only starts and stops it. Registered only when
/// <c>CAMERA_MODE=ustreamer</c> — mock deployments register nothing
/// camera-related. Mirrors <see cref="SafetyLifecycleHostedService"/>.
/// </summary>
public sealed class CameraMonitorHostedService(CameraStatusMonitor monitor) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        monitor.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => monitor.StopAsync(cancellationToken);
}
