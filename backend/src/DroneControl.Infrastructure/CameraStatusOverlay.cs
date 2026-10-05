using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Camera-only status decorator (specs/hardware/camera-runtime.md, Task 31,
/// <c>CAMERA_MODE=ustreamer</c>). Replaces <c>State.Camera</c> with the
/// monitor's observed pipeline health and delegates everything else — every
/// other status field and every operation — unchanged to the inner controller.
/// <para>
/// The overlay is composed <b>below</b> <see cref="DroneSafetyController"/>,
/// so the safety-closed <c>Camera = Offline</c> override still wins over any
/// probe value. It performs no network I/O of its own: reads come from the
/// monitor's cache.
/// </para>
/// </summary>
public sealed class CameraStatusOverlay : IDroneController
{
    private readonly IDroneController _inner;
    private readonly CameraStatusMonitor _monitor;

    public CameraStatusOverlay(IDroneController inner, CameraStatusMonitor monitor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    /// <inheritdoc />
    public async Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _inner.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        // Only State.Camera changes; RaspberryPi, Api, and every other
        // DroneState field pass through byte-identical.
        return status with
        {
            State = status.State with { Camera = _monitor.Current },
        };
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => _inner.ConnectAsync(cancellationToken);

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _inner.DisconnectAsync(cancellationToken);

    /// <inheritdoc />
    public Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
        => _inner.SendCommandAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
        => _inner.SetSpeedAsync(speed, cancellationToken);
}
