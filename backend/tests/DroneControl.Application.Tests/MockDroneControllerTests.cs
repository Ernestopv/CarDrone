using System.Diagnostics;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Pins the simulator contract from specs/backend/drone-simulator.md: the
/// connection lifecycle, epoch supersession, cancellation reset, requested vs
/// confirmed command semantics with the 700 ms / 250 ms delays as lower-bound
/// only assertions (jitter headroom), and the speed paths. A fresh
/// MockDroneController per test keeps the stateful singleton isolated.
/// </summary>
public class MockDroneControllerTests
{
    // Named-constant lower bounds with headroom; never upper bounds.
    private const int ConnectFloorMs = 600;
    private const int AckFloorMs = 150;

    private static async Task ConnectAndAssertAsync(MockDroneController controller)
    {
        var sw = Stopwatch.StartNew();
        await controller.ConnectAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= ConnectFloorMs,
            $"connection completed in {sw.ElapsedMilliseconds} ms, below the {ConnectFloorMs} ms floor");
        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
        Assert.Equal(CameraStatus.Streaming, status.State.Camera);
        Assert.Equal(ConnectionStatus.Connected, status.RaspberryPi);
    }

    [Fact]
    public async Task Initial_Status_IsFullyOffline_AndApiNotStamped()
    {
        var controller = new MockDroneController();

        var status = await controller.GetStatusAsync();

        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(CameraStatus.Offline, status.State.Camera);
        Assert.Equal(DroneCommand.Stop, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
        Assert.Equal(0, status.State.Speed);
        Assert.Equal(ConnectionStatus.Offline, status.RaspberryPi);
        Assert.Equal(ConnectionStatus.Offline, status.Api);
    }

    [Fact]
    public async Task Connect_TransitionsToConnecting_BeforeCompletion()
    {
        var controller = new MockDroneController();

        // ConnectAsync sets the connecting state synchronously before its
        // first suspension, so the status read between call and await must
        // observe it (drone, camera and Raspberry Pi all report connecting).
        var pending = controller.ConnectAsync();

        var mid = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Connecting, mid.State.Connection);
        Assert.Equal(CameraStatus.Connecting, mid.State.Camera);
        Assert.Equal(ConnectionStatus.Connecting, mid.RaspberryPi);

        await pending;
    }

    [Fact]
    public async Task Connect_CompletesToConnected_AfterLowerBoundDelay()
    {
        var controller = new MockDroneController();

        await ConnectAndAssertAsync(controller);
    }

    [Fact]
    public async Task RepeatConnect_WhileConnecting_IsGuardedNoOp()
    {
        var controller = new MockDroneController();
        var first = controller.ConnectAsync();

        var second = controller.ConnectAsync();
        Assert.True(second.IsCompleted, "a guarded repeat connect must return immediately");

        await first;
        await second;

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
    }

    [Fact]
    public async Task RepeatConnect_WhileConnected_NeverResetsLiveConnection()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);

        await controller.ConnectAsync();

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
    }

    [Fact]
    public async Task Disconnect_ResetsImmediatelyToInitial()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);
        await controller.SetSpeedAsync(60);

        var disconnect = controller.DisconnectAsync();
        Assert.True(disconnect.IsCompleted, "disconnect is immediate");

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(CameraStatus.Offline, status.State.Camera);
        Assert.Equal(0, status.State.Speed);
        Assert.Equal(ConnectionStatus.Offline, status.RaspberryPi);
    }

    [Fact]
    public async Task Disconnect_SupersedesInFlightConnect_AndNeverResurrectsIt()
    {
        var controller = new MockDroneController();

        var pending = controller.ConnectAsync();
        await controller.DisconnectAsync();
        await pending; // completes without error but must not apply

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(CameraStatus.Offline, status.State.Camera);
        Assert.Equal(ConnectionStatus.Offline, status.RaspberryPi);
    }

    [Fact]
    public async Task CancelledConnect_FaultsAndRestoresInitialState()
    {
        var controller = new MockDroneController();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.ConnectAsync(cts.Token));

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(CameraStatus.Offline, status.State.Camera);
        Assert.Equal(ConnectionStatus.Offline, status.RaspberryPi);
    }

    [Fact]
    public async Task Command_WhileOffline_ThrowsNotConnected()
    {
        var controller = new MockDroneController();

        await Assert.ThrowsAsync<DroneNotConnectedException>(
            () => controller.SendCommandAsync(DroneCommand.Forward));
    }

    [Fact]
    public async Task Command_WhileConnecting_ThrowsNotConnected()
    {
        var controller = new MockDroneController();
        var pending = controller.ConnectAsync();

        await Assert.ThrowsAsync<DroneNotConnectedException>(
            () => controller.SendCommandAsync(DroneCommand.Forward));

        await pending;
    }

    [Fact]
    public async Task Command_WhenConnected_RecordsRequestedImmediately_ConfirmsAfterLowerBound()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);

        var sw = Stopwatch.StartNew();
        var pending = controller.SendCommandAsync(DroneCommand.Forward);

        var mid = await controller.GetStatusAsync();
        Assert.Equal(DroneCommand.Forward, mid.State.RequestedCommand);
        Assert.Equal(DroneCommand.Stop, mid.State.ConfirmedCommand);

        await pending;
        sw.Stop();

        var after = await controller.GetStatusAsync();
        Assert.Equal(DroneCommand.Forward, after.State.ConfirmedCommand);
        Assert.True(sw.ElapsedMilliseconds >= AckFloorMs,
            $"ack completed in {sw.ElapsedMilliseconds} ms, below the {AckFloorMs} ms floor");
    }

    [Fact]
    public async Task ConcurrentCommands_AreConfirmedInRequestOrder()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);

        var first = controller.SendCommandAsync(DroneCommand.Forward);
        var second = controller.SendCommandAsync(DroneCommand.Backward);

        await first;
        var afterFirst = await controller.GetStatusAsync();
        Assert.Equal(DroneCommand.Forward, afterFirst.State.ConfirmedCommand);

        await second;
        var afterSecond = await controller.GetStatusAsync();
        Assert.Equal(DroneCommand.Backward, afterSecond.State.ConfirmedCommand);
        Assert.Equal(DroneCommand.Backward, afterSecond.State.RequestedCommand);
    }

    [Fact]
    public async Task Disconnect_DuringInFlightAck_SkipsStaleConfirmation()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);

        var pending = controller.SendCommandAsync(DroneCommand.Forward);
        await controller.DisconnectAsync();
        await pending; // completes, but the epoch mismatch skips applying it

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
        Assert.Equal(DroneCommand.Stop, status.State.RequestedCommand);
    }

    [Fact]
    public async Task Speed_WhileOffline_IsNoOp()
    {
        var controller = new MockDroneController();

        await controller.SetSpeedAsync(50);

        var status = await controller.GetStatusAsync();
        Assert.Equal(0, status.State.Speed);
    }

    [Fact]
    public async Task Speed_OutOfRange_WhileOffline_IsNoOpNotThrow()
    {
        // Documented quirk (specs/backend/drone-simulator.md): the connection
        // gate runs before the range check, so offline rejects are silent.
        var controller = new MockDroneController();

        await controller.SetSpeedAsync(120);

        var status = await controller.GetStatusAsync();
        Assert.Equal(0, status.State.Speed);
    }

    [Fact]
    public async Task Speed_WhenConnected_SetsInRange()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);

        await controller.SetSpeedAsync(55);

        var status = await controller.GetStatusAsync();
        Assert.Equal(55, status.State.Speed);
    }

    [Fact]
    public async Task Speed_WhenConnected_OutOfRange_ThrowsAndLeavesSpeedUnchanged()
    {
        var controller = new MockDroneController();
        await ConnectAndAssertAsync(controller);
        await controller.SetSpeedAsync(55);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => controller.SetSpeedAsync(101));

        var status = await controller.GetStatusAsync();
        Assert.Equal(55, status.State.Speed);
    }
}
