using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Application.Tests;

/// <summary>
/// Hand-written recording fake (no mocking library): captures every call and
/// token, and can be scripted to throw. Used to prove DroneService delegation
/// rules from specs/backend/drone-application-service.md.
/// </summary>
public sealed class FakeDroneController : IDroneController
{
    public List<string> Calls { get; } = [];

    public DroneStatus NextStatus { get; set; } = new();

    public Exception? ThrowOnSend { get; set; }

    public Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetStatusAsync));
        return Task.FromResult(NextStatus);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ConnectAsync));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(DisconnectAsync));
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(SendCommandAsync)}:{command}");
        if (ThrowOnSend is { } ex)
        {
            return Task.FromException(ex);
        }

        return Task.CompletedTask;
    }

    public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(SetSpeedAsync)}:{speed}");
        return Task.CompletedTask;
    }
}

public class DroneServiceTests
{
    [Fact]
    public async Task GetState_StampsApiConnected_AndPreservesEverythingElse()
    {
        var controller = new FakeDroneController
        {
            NextStatus = new DroneStatus
            {
                State = new DroneState
                {
                    Connection = ConnectionStatus.Connected,
                    Camera = CameraStatus.Streaming,
                    RequestedCommand = DroneCommand.Left,
                    ConfirmedCommand = DroneCommand.Right,
                    Speed = 42,
                },
                RaspberryPi = ConnectionStatus.Connected,
                Api = ConnectionStatus.Error,
            },
        };
        var service = new DroneService(controller);

        var status = await service.GetStateAsync();

        Assert.Equal(ConnectionStatus.Connected, status.Api);
        Assert.Equal(ConnectionStatus.Connected, status.RaspberryPi);
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
        Assert.Equal(CameraStatus.Streaming, status.State.Camera);
        Assert.Equal(DroneCommand.Left, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Right, status.State.ConfirmedCommand);
        Assert.Equal(42, status.State.Speed);
    }

    [Fact]
    public async Task Connect_DelegatesToController()
    {
        var controller = new FakeDroneController();
        var service = new DroneService(controller);
        using var cts = new CancellationTokenSource();

        await service.ConnectAsync(cts.Token);

        Assert.Contains(nameof(FakeDroneController.ConnectAsync), controller.Calls);
    }

    [Fact]
    public async Task Disconnect_DelegatesToController()
    {
        var controller = new FakeDroneController();
        var service = new DroneService(controller);

        await service.DisconnectAsync();

        Assert.Contains(nameof(FakeDroneController.DisconnectAsync), controller.Calls);
    }

    [Fact]
    public async Task SendCommand_DelegatesCommandUntouched()
    {
        var controller = new FakeDroneController();
        var service = new DroneService(controller);

        await service.SendCommandAsync(DroneCommand.Forward);

        Assert.Contains($"{nameof(FakeDroneController.SendCommandAsync)}:Forward", controller.Calls);
    }

    [Fact]
    public async Task SendCommand_PropagatesControllerException()
    {
        var controller = new FakeDroneController
        {
            ThrowOnSend = new DroneNotConnectedException(),
        };
        var service = new DroneService(controller);

        await Assert.ThrowsAsync<DroneNotConnectedException>(
            () => service.SendCommandAsync(DroneCommand.Forward));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(55)]
    [InlineData(100)]
    public async Task SetSpeed_InRange_Delegates(int speed)
    {
        var controller = new FakeDroneController();
        var service = new DroneService(controller);

        await service.SetSpeedAsync(speed);

        Assert.Contains($"{nameof(FakeDroneController.SetSpeedAsync)}:{speed}", controller.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SetSpeed_OutOfRange_Throws_BeforeControllerIsCalled(int speed)
    {
        var controller = new FakeDroneController();
        var service = new DroneService(controller);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetSpeedAsync(speed));

        Assert.DoesNotContain(controller.Calls, c => c.StartsWith(nameof(FakeDroneController.SetSpeedAsync)));
    }
}
