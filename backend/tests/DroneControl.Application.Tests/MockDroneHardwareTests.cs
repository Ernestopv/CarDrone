using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Unit tests for the hardware seam (specs/hardware/hardware-abstraction.md):
/// availability, software-level command outcomes, speed validation without
/// clamping, and the documented "unavailable throws" contract rule.
/// </summary>
public class MockDroneHardwareTests
{
    /// <summary>
    /// A deliberately unreachable hardware implementation used to pin the
    /// interface contract every real provider must honor: unreachable means
    /// loud <see cref="DroneUnavailableException"/>, never a quiet rejection.
    /// </summary>
    private sealed class UnreachableHardware : IDroneHardware
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

    [Fact]
    public async Task Mock_AlwaysReportsAvailable()
    {
        var hardware = new MockDroneHardware();

        Assert.Equal(HardwareAvailability.Available, await hardware.GetAvailabilityAsync());
    }

    [Fact]
    public void Mock_RecordsNothingBeforeFirstOperation()
    {
        var hardware = new MockDroneHardware();

        Assert.Null(hardware.LastAppliedCommand);
        Assert.Equal(0, hardware.LastAppliedSpeedPercent);
    }

    [Theory]
    [InlineData(DroneCommand.Forward)]
    [InlineData(DroneCommand.Stop)]
    public async Task ExecuteCommand_ReturnsApplied_AndRecordsTheCommand(DroneCommand command)
    {
        var hardware = new MockDroneHardware();

        var result = await hardware.ExecuteCommandAsync(command);

        Assert.Equal(HardwareCommandStatus.Applied, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal(command, hardware.LastAppliedCommand);
    }

    [Fact]
    public async Task ExecuteCommand_KeepsOnlyTheLatestApplied()
    {
        var hardware = new MockDroneHardware();

        await hardware.ExecuteCommandAsync(DroneCommand.Forward);
        await hardware.ExecuteCommandAsync(DroneCommand.Backward);

        Assert.Equal(DroneCommand.Backward, hardware.LastAppliedCommand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task ApplySpeed_AcceptsRangeBoundaries(int speed)
    {
        var hardware = new MockDroneHardware();

        await hardware.ApplySpeedAsync(speed);

        Assert.Equal(speed, hardware.LastAppliedSpeedPercent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task ApplySpeed_RejectsOutOfRange_WithoutChangingState(int speed)
    {
        var hardware = new MockDroneHardware();
        await hardware.ApplySpeedAsync(55);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => hardware.ApplySpeedAsync(speed));

        Assert.Equal(55, hardware.LastAppliedSpeedPercent);
    }

    [Fact]
    public async Task UnreachableHardware_ReportsUnavailable()
    {
        IDroneHardware hardware = new UnreachableHardware();

        Assert.Equal(HardwareAvailability.Unavailable, await hardware.GetAvailabilityAsync());
    }

    [Fact]
    public async Task UnreachableHardware_ThrowsOnCommand_NeverSilentNoOp()
    {
        IDroneHardware hardware = new UnreachableHardware();

        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Forward));
    }

    [Fact]
    public async Task UnreachableHardware_ThrowsOnSpeed_NeverSilentNoOp()
    {
        IDroneHardware hardware = new UnreachableHardware();

        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));
    }

    [Fact]
    public async Task AllOperations_HonorCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var hardware = new MockDroneHardware();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hardware.GetAvailabilityAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Left, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hardware.ApplySpeedAsync(50, cts.Token));
    }
}
