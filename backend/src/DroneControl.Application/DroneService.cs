using DroneControl.Domain;

namespace DroneControl.Application;

/// <summary>
/// Stateless orchestration between the API and the drone controller: validates
/// at the application boundary, delegates, and stamps the API component as
/// connected when serving status. Controller exceptions propagate unchanged, so
/// success is never reported without the controller completing the operation.
/// </summary>
public sealed class DroneService : IDroneService
{
    private readonly IDroneController _controller;

    /// <summary>Creates the service over its single dependency.</summary>
    public DroneService(IDroneController controller)
    {
        _controller = controller;
    }

    /// <inheritdoc />
    public async Task<DroneStatus> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var status = await _controller.GetStatusAsync(cancellationToken);
        return status with { Api = ConnectionStatus.Connected };
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => _controller.ConnectAsync(cancellationToken);

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _controller.DisconnectAsync(cancellationToken);

    /// <inheritdoc />
    public Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
        => _controller.SendCommandAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
    {
        if (speed is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                speed,
                "Speed must be between 0 and 100.");
        }

        return _controller.SetSpeedAsync(speed, cancellationToken);
    }
}
