using DroneControl.Domain;

namespace DroneControl.Domain.Tests;

/// <summary>
/// Domain invariants from specs/backend/domain-model.md: initial values and
/// the speed range rule (0-100 inclusive, reject-don't-clamp, enforced in
/// both construction and record <c>with</c> updates).
/// </summary>
public class DroneStateTests
{
    [Fact]
    public void InitialState_IsAllOfflineStopAndZeroSpeed()
    {
        var state = new DroneState();

        Assert.Equal(ConnectionStatus.Offline, state.Connection);
        Assert.Equal(CameraStatus.Offline, state.Camera);
        Assert.Equal(DroneCommand.Stop, state.RequestedCommand);
        Assert.Equal(DroneCommand.Stop, state.ConfirmedCommand);
        Assert.Equal(0, state.Speed);
    }

    [Fact]
    public void InitialStatus_IsAllOffline()
    {
        var status = new DroneStatus();

        Assert.Equal(ConnectionStatus.Offline, status.RaspberryPi);
        Assert.Equal(ConnectionStatus.Offline, status.Api);
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Speed_AcceptsRangeBoundaries(int speed)
    {
        Assert.Equal(speed, new DroneState { Speed = speed }.Speed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Speed_RejectsOutOfRangeAtConstruction(int speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DroneState { Speed = speed });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Speed_RejectsOutOfRangeInWithUpdates(int speed)
    {
        var state = new DroneState { Speed = 50 };

        Assert.Throws<ArgumentOutOfRangeException>(() => state with { Speed = speed });
    }

    [Fact]
    public void Speed_WithUpdate_KeepsExactValue_NeverClamps()
    {
        var state = new DroneState { Speed = 42 };

        Assert.Equal(55, (state with { Speed = 55 }).Speed);
        Assert.Equal(42, state.Speed);
    }
}
