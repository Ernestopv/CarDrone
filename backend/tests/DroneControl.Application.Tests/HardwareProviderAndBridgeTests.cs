using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Unit tests for the Raspberry provider (inert) and the hardware-backed
/// controller bridge (specs/hardware/raspberry-hardware-provider.md). Fakes
/// stand in for both availability states so the full contract is exercised on
/// any dev machine; Pi-runtime behavior remains deliberately unverified.
/// </summary>
public class HardwareProviderAndBridgeTests
{
    /// <summary>A reachable, recording hardware layer; results are scriptable.</summary>
    private sealed class AvailableHardware : IDroneHardware
    {
        public List<string> Calls { get; } = [];

        public CommandExecutionResult NextResult { get; set; } =
            new(HardwareCommandStatus.Applied);

        public Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("availability");
            return Task.FromResult(HardwareAvailability.Available);
        }

        public Task<CommandExecutionResult> ExecuteCommandAsync(
            DroneCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"execute:{command}");
            return Task.FromResult(NextResult);
        }

        public Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default)
        {
            Calls.Add($"speed:{speedPercent}");
            return Task.CompletedTask;
        }
    }

    /// <summary>A hardware layer that refuses everything (mirrors the inert provider).</summary>
    private sealed class UnavailableHardware : IDroneHardware
    {
        public Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(HardwareAvailability.Unavailable);

        public Task<CommandExecutionResult> ExecuteCommandAsync(
            DroneCommand command,
            CancellationToken cancellationToken = default)
            => Task.FromException<CommandExecutionResult>(new DroneUnavailableException());

        public Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default)
            => Task.FromException(new DroneUnavailableException());
    }

    // --- RaspberryDroneHardware (inert provider) ---

    [Fact]
    public async Task Provider_ReportsUnavailable()
    {
        IDroneHardware provider = new RaspberryDroneHardware();

        Assert.Equal(HardwareAvailability.Unavailable, await provider.GetAvailabilityAsync());
    }

    [Fact]
    public async Task Provider_ExecuteCommand_ThrowsUnavailable_NeverSilent()
    {
        IDroneHardware provider = new RaspberryDroneHardware();

        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => provider.ExecuteCommandAsync(DroneCommand.Forward));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Provider_ApplySpeed_InvalidRangeThrowsBeforeUnavailability(int speed)
    {
        IDroneHardware provider = new RaspberryDroneHardware();

        // Task 22 rule: invariants are platform-independent and checked first.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.ApplySpeedAsync(speed));
    }

    [Fact]
    public async Task Provider_ApplySpeed_Valid_ThrowsUnavailable()
    {
        IDroneHardware provider = new RaspberryDroneHardware();

        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => provider.ApplySpeedAsync(45));
    }

    [Fact]
    public async Task Provider_AllOperations_HonorCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        IDroneHardware provider = new RaspberryDroneHardware();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAvailabilityAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExecuteCommandAsync(DroneCommand.Left, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ApplySpeedAsync(50, cts.Token));
    }

    // --- HardwareDroneController (bridge) ---

    [Fact]
    public async Task Bridge_InitialStatus_IsHonestBaseline()
    {
        var controller = new HardwareDroneController(new AvailableHardware());

        var status = await controller.GetStatusAsync();

        Assert.Equal(ConnectionStatus.Offline, status.State.Connection); // no session yet
        Assert.Equal(CameraStatus.Offline, status.State.Camera); // no video pipeline in this mode
        Assert.Equal(ConnectionStatus.Connected, status.RaspberryPi); // probed available
        Assert.Equal(ConnectionStatus.Offline, status.Api); // DroneService owns the stamp
        Assert.Equal(DroneCommand.Stop, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
        Assert.Equal(0, status.State.Speed);
    }

    [Fact]
    public async Task Bridge_Connect_WithAvailableHardware_BecomesConnected()
    {
        var controller = new HardwareDroneController(new AvailableHardware());

        await controller.ConnectAsync();

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
    }

    [Fact]
    public async Task Bridge_Connect_WithUnavailableHardware_ThrowsAndStaysOffline()
    {
        var controller = new HardwareDroneController(new UnavailableHardware());

        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => controller.ConnectAsync());

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
    }

    [Fact]
    public async Task Bridge_Command_WhenDisconnected_ThrowsNotConnected_AndIsNotForwarded()
    {
        var hardware = new AvailableHardware();
        var controller = new HardwareDroneController(hardware);

        await Assert.ThrowsAsync<DroneNotConnectedException>(
            () => controller.SendCommandAsync(DroneCommand.Forward));

        Assert.DoesNotContain(hardware.Calls, c => c.StartsWith("execute:"));
    }

    [Fact]
    public async Task Bridge_Command_Applied_ConfirmsImmediately()
    {
        var hardware = new AvailableHardware();
        var controller = new HardwareDroneController(hardware);
        await controller.ConnectAsync();

        await controller.SendCommandAsync(DroneCommand.Forward);

        var status = await controller.GetStatusAsync();
        Assert.Equal(DroneCommand.Forward, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Forward, status.State.ConfirmedCommand);
        Assert.Contains("execute:Forward", hardware.Calls);
    }

    [Fact]
    public async Task Bridge_Command_Rejected_ConfirmsNothing()
    {
        var hardware = new AvailableHardware
        {
            NextResult = new CommandExecutionResult(HardwareCommandStatus.Rejected, "not wired yet"),
        };
        var controller = new HardwareDroneController(hardware);
        await controller.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SendCommandAsync(DroneCommand.Backward));

        var status = await controller.GetStatusAsync();
        Assert.Equal(DroneCommand.Backward, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
    }

    [Fact]
    public async Task Bridge_Disconnect_ResetsTheSession()
    {
        var hardware = new AvailableHardware();
        var controller = new HardwareDroneController(hardware);
        await controller.ConnectAsync();
        await controller.SendCommandAsync(DroneCommand.Right);
        await controller.SetSpeedAsync(60);

        await controller.DisconnectAsync();

        var status = await controller.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(DroneCommand.Stop, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
        Assert.Equal(0, status.State.Speed);
    }

    [Fact]
    public async Task Bridge_SetSpeed_WhenDisconnected_IsSilentNoOp_ParityWithSimulator()
    {
        var hardware = new AvailableHardware();
        var controller = new HardwareDroneController(hardware);

        await controller.SetSpeedAsync(50);

        var status = await controller.GetStatusAsync();
        Assert.Equal(0, status.State.Speed);
        Assert.DoesNotContain(hardware.Calls, c => c.StartsWith("speed:"));
    }

    [Fact]
    public async Task Bridge_SetSpeed_Connected_ForwardsAndRecords()
    {
        var hardware = new AvailableHardware();
        var controller = new HardwareDroneController(hardware);
        await controller.ConnectAsync();

        await controller.SetSpeedAsync(45);

        var status = await controller.GetStatusAsync();
        Assert.Equal(45, status.State.Speed);
        Assert.Contains("speed:45", hardware.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Bridge_SetSpeed_InvalidRange_ThrowsRegardlessOfSessionState(int speed)
    {
        var controller = new HardwareDroneController(new AvailableHardware());

        // Not even connected — the range invariant still fires (spec order).
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => controller.SetSpeedAsync(speed));
    }
}
