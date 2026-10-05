using System.Collections.Concurrent;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Mock/fake-only tests for Task 26 software safety policy. These verify
/// semantic STOP requests and coordinator state; they do NOT prove physical
/// stop behavior or any GPIO/electrical semantics.
/// </summary>
public class DroneSafetyControllerTests
{
    private sealed class FakeDroneController : IDroneController
    {
        private readonly object _gate = new();
        private bool _connected;
        private DroneCommand _requested = DroneCommand.Stop;
        private DroneCommand _confirmed = DroneCommand.Stop;
        private int _speed;

        public ConcurrentQueue<string> Calls { get; } = new();
        public TimeSpan MovementDelay { get; set; }
        public Exception? MovementFailure { get; set; }
        public int StopFailuresRemaining { get; set; }
        public int ConnectFailuresRemaining { get; set; }
        public bool StopReturnsWithoutConfirmation { get; set; }

        public Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(new DroneStatus
                {
                    State = new DroneState
                    {
                        Connection = _connected ? ConnectionStatus.Connected : ConnectionStatus.Offline,
                        Camera = CameraStatus.Offline,
                        RequestedCommand = _requested,
                        ConfirmedCommand = _confirmed,
                        Speed = _speed,
                    },
                    RaspberryPi = ConnectionStatus.Offline,
                });
            }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue("connect");
            if (ConnectFailuresRemaining > 0)
            {
                ConnectFailuresRemaining--;
                return Task.FromException(new DroneUnavailableException());
            }

            lock (_gate)
            {
                _connected = true;
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue("disconnect");
            lock (_gate)
            {
                _connected = false;
                _requested = DroneCommand.Stop;
                _confirmed = DroneCommand.Stop;
                _speed = 0;
            }

            return Task.CompletedTask;
        }

        public async Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue($"command:{command}");
            lock (_gate)
            {
                if (!_connected)
                {
                    throw new DroneNotConnectedException();
                }

                _requested = command;
            }

            if (command != DroneCommand.Stop && MovementDelay > TimeSpan.Zero)
            {
                await Task.Delay(MovementDelay, cancellationToken);
            }

            if (command != DroneCommand.Stop && MovementFailure is { } failure)
            {
                throw failure;
            }

            lock (_gate)
            {
                if (command == DroneCommand.Stop && StopFailuresRemaining > 0)
                {
                    StopFailuresRemaining--;
                    throw new DroneUnavailableException();
                }

                if (command == DroneCommand.Stop && StopReturnsWithoutConfirmation)
                {
                    _requested = DroneCommand.Stop;
                    return;
                }

                _confirmed = command;
            }
        }

        public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue($"speed:{speed}");
            lock (_gate)
            {
                if (_connected)
                {
                    _speed = speed;
                }
            }

            return Task.CompletedTask;
        }
    }

    private static DroneSafetyOptions Options(
        int commandTimeoutMs = 500,
        int stopTimeoutMs = 100,
        int stopRetries = 0,
        int stopRetryDelayMs = 0,
        int recoveryRetries = 0,
        int recoveryRetryDelayMs = 0,
        bool realMode = false,
        bool externalProtectionVerified = false)
        => new(commandTimeoutMs, stopTimeoutMs, stopRetries, stopRetryDelayMs,
            recoveryRetries, recoveryRetryDelayMs, realMode, externalProtectionVerified);

    private static DroneSafetyController Create(FakeDroneController inner, DroneSafetyOptions? options = null)
        => new(inner, options ?? Options(), _ => { });

    private static int StopCount(FakeDroneController inner)
        => inner.Calls.Count(call => call == "command:Stop");

    [Theory]
    [InlineData(0, 100, 0, 0, 0, 0)]
    [InlineData(30_001, 100, 0, 0, 0, 0)]
    [InlineData(100, 5_001, 0, 0, 0, 0)]
    [InlineData(100, 100, -1, 0, 0, 0)]
    [InlineData(100, 100, 4, 0, 0, 0)]
    [InlineData(100, 100, 0, 2_001, 0, 0)]
    public void SafetyOptions_RejectInvalidOrUnboundedValues(
        int commandMs, int stopMs, int stopRetries, int stopDelayMs, int recoveryRetries, int recoveryDelayMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DroneSafetyOptions(
            commandMs, stopMs, stopRetries, stopDelayMs, recoveryRetries, recoveryDelayMs));
    }

    private static async Task WaitForStateAsync(
        DroneSafetyController controller,
        DroneSafetyState expected,
        int timeoutMilliseconds = 2_000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (controller.CurrentSafetyState == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Equal(expected, controller.CurrentSafetyState);
    }

    [Theory]
    [InlineData(0, 100, 0, 0, 0, 0)]
    [InlineData(31_000, 100, 0, 0, 0, 0)]
    [InlineData(100, 5_001, 0, 0, 0, 0)]
    [InlineData(100, 100, 4, 0, 0, 0)]
    [InlineData(100, 100, 0, 2_001, 0, 0)]
    public void Options_RejectMissingRangeOrExcessiveSafetyLimits(
        int command, int stop, int stopRetries, int stopDelay, int recoveryRetries, int recoveryDelay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DroneSafetyOptions(command, stop, stopRetries, stopDelay, recoveryRetries, recoveryDelay));
    }

    [Fact]
    public async Task Startup_EstablishesStopBaselineThenLeavesSessionOffline()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner);

        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal(new[] { "connect", "command:Stop", "disconnect" }, inner.Calls.ToArray());
        var status = await safety.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
    }

    [Fact]
    public async Task StartupFailure_LeavesCoordinatorFaultedAndBlocksMovement()
    {
        var inner = new FakeDroneController { ConnectFailuresRemaining = 1 };
        var safety = Create(inner);

        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => safety.SendCommandAsync(DroneCommand.Forward));
    }

    [Fact]
    public async Task RealModeWithoutExternalProtection_IsFaultedBeforeAnyControllerCall()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(realMode: true, externalProtectionVerified: false));

        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        Assert.Empty(inner.Calls);
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => safety.SendCommandAsync(DroneCommand.Stop));
        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        await Assert.ThrowsAsync<DroneUnavailableException>(() => safety.ConnectAsync());
        Assert.Empty(inner.Calls);
    }

    [Fact]
    public async Task RealModeProtectionAssertion_OnlyOpensSoftwareGate()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(realMode: true, externalProtectionVerified: true));

        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Contains("command:Stop", inner.Calls);
        // This fake proves the software gate only, not the asserted external
        // mechanism or any physical stop behavior.
    }

    [Fact]
    public async Task RealMode_WithoutExternalAbruptFailureProtection_IsFailClosedBeforeHardwareCalls()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(realMode: true, externalProtectionVerified: false));

        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        Assert.Empty(inner.Calls);
        await Assert.ThrowsAsync<DroneUnavailableException>(() => safety.ConnectAsync());
        Assert.Empty(inner.Calls);
    }

    [Fact]
    public async Task RealMode_CanPassSafetyGateOnlyWhenExternalProtectionIsExplicitlyAsserted()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(realMode: true, externalProtectionVerified: true));

        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Contains("command:Stop", inner.Calls);
        // This is fake-controller software evidence only; it proves no physical
        // watchdog or electrical behavior.
    }

    [Fact]
    public async Task Connect_EstablishesStopBaselineBeforeReturning()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner);
        await safety.StartSafetyAsync();

        await safety.ConnectAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal("command:Stop", inner.Calls.Last());
        Assert.Equal(ConnectionStatus.Connected, (await safety.GetStatusAsync()).State.Connection);
    }

    [Fact]
    public async Task MovementCommand_EntersActive_ThenLivenessDeadlineRequestsStop()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(commandTimeoutMs: 80));
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();

        await safety.SendCommandAsync(DroneCommand.Forward);
        Assert.Equal(DroneSafetyState.CommandActive, safety.CurrentSafetyState);

        await WaitForStateAsync(safety, DroneSafetyState.Safe);
        Assert.True(StopCount(inner) >= 3); // startup, connect baseline, liveness stop
        Assert.Equal(DroneCommand.Stop, (await inner.GetStatusAsync()).State.ConfirmedCommand);
    }

    [Fact]
    public async Task MovementOperationTimeout_CancelsOperationAndRequestsStop()
    {
        var inner = new FakeDroneController { MovementDelay = TimeSpan.FromSeconds(2) };
        var safety = Create(inner, Options(commandTimeoutMs: 40, stopTimeoutMs: 200));
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => safety.SendCommandAsync(DroneCommand.Forward));

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal(DroneCommand.Stop, (await inner.GetStatusAsync()).State.ConfirmedCommand);
    }

    [Fact]
    public async Task MovementFailure_AttemptsStopAndPreservesOriginalFailure()
    {
        var original = new InvalidOperationException("synthetic command failure");
        var inner = new FakeDroneController { MovementFailure = original };
        var safety = Create(inner);
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => safety.SendCommandAsync(DroneCommand.Left));

        Assert.Same(original, thrown);
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal(DroneCommand.Stop, (await inner.GetStatusAsync()).State.ConfirmedCommand);
    }

    [Fact]
    public async Task StopFailure_FaultsAndBlocksFurtherMovement()
    {
        var inner = new FakeDroneController { MovementFailure = new IOException("command failed") };
        var safety = Create(inner, Options(stopRetries: 1));
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        inner.StopFailuresRemaining = 2;

        await Assert.ThrowsAsync<IOException>(() => safety.SendCommandAsync(DroneCommand.Forward));

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => safety.SendCommandAsync(DroneCommand.Backward));
        Assert.Equal(4, StopCount(inner)); // two baseline stops + two bounded failure attempts
    }

    [Fact]
    public async Task StopWithoutControllerConfirmation_IsNotAcceptedAsSafe()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(commandTimeoutMs: 60, stopRetries: 1));
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        await safety.SendCommandAsync(DroneCommand.Forward);
        inner.StopReturnsWithoutConfirmation = true;

        await WaitForStateAsync(safety, DroneSafetyState.Faulted);

        Assert.Equal(DroneCommand.Forward, (await inner.GetStatusAsync()).State.ConfirmedCommand);
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => safety.SendCommandAsync(DroneCommand.Backward));
    }

    [Fact]
    public async Task InvalidCommand_DoesNotCancelAnActiveMovementDeadline()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(commandTimeoutMs: 80));
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        await safety.SendCommandAsync(DroneCommand.Forward);
        var stopsBefore = StopCount(inner);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => safety.SendCommandAsync((DroneCommand)999));
        await WaitForStateAsync(safety, DroneSafetyState.Safe);

        Assert.Equal(stopsBefore + 1, StopCount(inner));
    }

    [Fact]
    public async Task NewCommandSupersedesOldWatchdog_StaleDeadlineCannotStopCurrentCommand()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner, Options(commandTimeoutMs: 120));
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();

        await safety.SendCommandAsync(DroneCommand.Forward);
        await Task.Delay(60);
        await safety.SendCommandAsync(DroneCommand.Backward);
        var stopsBeforeCurrentDeadline = StopCount(inner);

        await Task.Delay(80); // past first deadline, before second deadline
        Assert.Equal(DroneSafetyState.CommandActive, safety.CurrentSafetyState);
        Assert.Equal(stopsBeforeCurrentDeadline, StopCount(inner));

        await WaitForStateAsync(safety, DroneSafetyState.Safe);
        Assert.Equal(stopsBeforeCurrentDeadline + 1, StopCount(inner));
    }

    [Fact]
    public async Task Disconnect_StopsBeforeResettingControllerSession()
    {
        var inner = new FakeDroneController();
        var safety = Create(inner);
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        await safety.SendCommandAsync(DroneCommand.Forward);

        await safety.DisconnectAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal("disconnect", inner.Calls.Last());
        Assert.Equal(DroneCommand.Stop, (await inner.GetStatusAsync()).State.ConfirmedCommand);
    }

    [Fact]
    public async Task GracefulShutdownAttemptsStopAndLogsSoftwareOutcome()
    {
        var events = new List<DroneSafetyEvent>();
        var inner = new FakeDroneController();
        var safety = new DroneSafetyController(inner, Options(), events.Add);
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        await safety.SendCommandAsync(DroneCommand.Right);

        await safety.StopSafetyAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal(DroneCommand.Stop, (await inner.GetStatusAsync()).State.ConfirmedCommand);
        Assert.Contains(events, e => e.Reason == DroneSafetyReason.GracefulShutdown
            && e.Message.Contains("physical stop is unverified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShutdownStopFailure_IsLoggedAndLeavesFaulted()
    {
        var events = new List<DroneSafetyEvent>();
        var inner = new FakeDroneController();
        var safety = new DroneSafetyController(inner, Options(), events.Add);
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        inner.StopFailuresRemaining = 1;

        await safety.StopSafetyAsync();

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        Assert.Contains(events, e => e.State == DroneSafetyState.Faulted
            && e.Reason == DroneSafetyReason.HardwareUnavailable);
    }

    [Fact]
    public async Task FailedRecoveryRemainsFaulted_ThenExplicitConnectCanRecover()
    {
        var inner = new FakeDroneController { ConnectFailuresRemaining = 5 };
        var safety = Create(inner, Options(recoveryRetries: 0));

        await safety.StartSafetyAsync();
        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);

        inner.ConnectFailuresRemaining = 0;
        await safety.ConnectAsync();

        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
    }

    [Fact]
    public async Task FaultedStatusUsesExistingErrorStateWithoutChangingWireShape()
    {
        var inner = new FakeDroneController { ConnectFailuresRemaining = 1 };
        var safety = Create(inner, Options(recoveryRetries: 0));

        await safety.StartSafetyAsync();

        var status = await safety.GetStatusAsync();
        Assert.Equal(ConnectionStatus.Error, status.State.Connection);
        Assert.Equal(CameraStatus.Offline, status.State.Camera);
    }
}
