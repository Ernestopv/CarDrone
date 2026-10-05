using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// PC-only dry-run tests (specs/hardware/dry-run.md). These prove composition,
/// record invariants, ordering, rejection parity, and fail-closed suppression
/// using fakes. They do NOT prove that a real Pi cannot energize a motor, that
/// physical STOP works, or any GPIO/PWM electrical behavior.
/// </summary>
public class DryRunOutputTests
{
    private static Dictionary<string, string?> ValidSection() => new()
    {
        ["Pin1"] = "23",
        ["Pin2"] = "24",
        ["Pin3"] = "21",
        ["Pin4"] = "20",
        ["PWM1"] = "12",
        ["PWM2"] = "13",
    };

    private static GpioPinConfiguration ValidConfig()
        => GpioPinConfiguration.FromSection(ValidSection());

    /// <summary>
    /// Test-only double representing the real path's intended operation
    /// sequence. It records calls; it performs no hardware work and encodes no
    /// motor semantics.
    /// </summary>
    private sealed class RecordingRealPathFake : IGpioController
    {
        public List<string> Calls { get; } = [];

        public void ConfigureOutput(int pin) => Calls.Add($"configure:{pin}");

        public void Write(int pin, GpioPinValue value) => Calls.Add($"write:{pin}:{value}");

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Test-only stand-in for the Task 28-30 command-to-operation mappings.
    /// It deliberately encodes NO direction, level, or motor meaning: every
    /// semantic command merely (re)configures the configured lines through the
    /// supplied output sink, which produces observable intended operations.
    /// </summary>
    private sealed class FakeOutputProducerHardware(IGpioController sink, GpioPinConfiguration config) : IDroneHardware
    {
        public Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HardwareAvailability.Available);
        }

        public Task<CommandExecutionResult> ExecuteCommandAsync(
            DroneCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var pin in config.DigitalLineIdentifiers)
            {
                sink.ConfigureOutput(pin);
            }

            return Task.FromResult(new CommandExecutionResult(HardwareCommandStatus.Applied));
        }

        public Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (speedPercent is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(speedPercent), speedPercent, "Speed must be between 0 and 100.");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Test-only real-output sink double; must stay untouched in dry-run graphs.</summary>
    private sealed class RecordingRealPlatform : IRaspberryGpioPlatform
    {
        public List<string> Calls { get; } = [];

        public void ConfigureOutput(int lineIdentifier) => Calls.Add($"configure:{lineIdentifier}");

        public void Write(int lineIdentifier, GpioPinValue value)
            => Calls.Add($"write:{lineIdentifier}:{value}");

        public void Dispose() => Calls.Add("dispose");
    }

    private static async Task WaitForStateAsync(
        DroneSafetyController safety,
        DroneSafetyState expected,
        int timeoutMilliseconds = 3_000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (safety.CurrentSafetyState == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Equal(expected, safety.CurrentSafetyState);
    }

    [Fact]
    public void EveryRecord_IsDryRunAndNeverPhysicallyApplied_WithoutConfirmationLanguage()
    {
        var records = new List<DryRunOperationRecord>();
        var config = ValidConfig();
        using var sink = new DryRunGpioController(config, records.Add);

        sink.ConfigureOutput(config.Pin1);
        sink.Write(config.Pin1, GpioPinValue.High);
        sink.Write(config.Pin1, GpioPinValue.Low);

        Assert.Equal(3, records.Count);
        Assert.All(records, record =>
        {
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
            Assert.Equal(HardwareOutputCategory.Gpio, record.Category);
            Assert.False(string.IsNullOrWhiteSpace(record.SuppressionReason));
            Assert.Contains("suppress", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);

            // The record/log vocabulary must never claim confirmation.
            Assert.DoesNotContain("confirm", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("acknowledg", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
        });

        Assert.All(records, record => Assert.False(record.DryRun && record.PhysicallyApplied));
        Assert.Equal(new[] { 1L, 2L, 3L }, records.Select(record => record.Sequence).ToArray());
        Assert.True(records.Zip(records.Skip(1)).All(pair => pair.First.TimestampUtc <= pair.Second.TimestampUtc));
    }

    [Fact]
    public void IdentifiersAndParameters_AreForwardedUnchanged_FromTheHardwarePath()
    {
        var records = new List<DryRunOperationRecord>();
        var config = ValidConfig();
        using var sink = new DryRunGpioController(config, records.Add);

        sink.ConfigureOutput(config.Pin3);
        sink.Write(config.Pin3, GpioPinValue.High);

        Assert.Equal(config.Pin3.ToString(), records[0].Identifier);
        Assert.Equal("configure-output", records[0].RequestedParameters);
        Assert.Equal(config.Pin3.ToString(), records[1].Identifier);
        Assert.Equal("write value=High", records[1].RequestedParameters);
    }

    [Fact]
    public void SameOperationSequence_AsRealPathFake_WhileEveryOutputIsSuppressed()
    {
        var config = ValidConfig();
        var records = new List<DryRunOperationRecord>();
        using var drySink = new DryRunGpioController(config, records.Add);
        using var realPathFake = new RecordingRealPathFake();

        // Identical intended-operation script through both sinks: only the
        // final sink differs (specs/hardware/dry-run.md Interfaces).
        void Script(IGpioController sink)
        {
            sink.ConfigureOutput(config.Pin1);
            sink.Write(config.Pin1, GpioPinValue.High);
            sink.Write(config.Pin1, GpioPinValue.Low);
            sink.ConfigureOutput(config.Pin2);
            sink.Write(config.Pin2, GpioPinValue.High);
        }

        Script(realPathFake);
        Script(drySink);

        Assert.Equal(5, realPathFake.Calls.Count);
        Assert.Equal(
            realPathFake.Calls,
            records.Select(record => record.RequestedParameters == "configure-output"
                ? $"configure:{record.Identifier}"
                : $"write:{record.Identifier}:{record.RequestedParameters["write value=".Length..]}")
                .ToArray());
        Assert.All(records, record =>
        {
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
        });
    }

    [Fact]
    public void InvalidUnconfiguredAndUnknownOperations_AreRejectedAndLogged_WithRealModeParity()
    {
        var records = new List<DryRunOperationRecord>();
        var config = ValidConfig();
        using var drySink = new DryRunGpioController(config, records.Add);
        using var realPathFake = new RecordingRealPathFake();
        using var mockSink = new MockGpioController(config);

        // Unknown identifier: same exception type as the mock/real contract.
        var dryUnknown = Assert.Throws<ArgumentException>(() => drySink.ConfigureOutput(999));
        var mockUnknown = Assert.Throws<ArgumentException>(() => mockSink.ConfigureOutput(999));
        Assert.Equal(mockUnknown.Message, dryUnknown.Message);

        // Write before configure: same exception type as the mock/real contract.
        var dryUnconfigured = Assert.Throws<InvalidOperationException>(
            () => drySink.Write(config.Pin1, GpioPinValue.High));
        var mockUnconfigured = Assert.Throws<InvalidOperationException>(
            () => mockSink.Write(config.Pin1, GpioPinValue.High));
        Assert.Equal(mockUnconfigured.Message, dryUnconfigured.Message);

        // Unknown output parameter (invalid enum): same exception type.
        Assert.Throws<ArgumentOutOfRangeException>(() => drySink.Write(config.Pin1, (GpioPinValue)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => mockSink.Write(config.Pin1, (GpioPinValue)42));

        // Every rejection is logged as suppressed/error — never executed.
        Assert.Equal(3, records.Count);
        Assert.All(records, record =>
        {
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
            Assert.Equal(DryRunGpioController.RejectedOperationReason, record.SuppressionReason);
        });
        Assert.Empty(realPathFake.Calls);
    }

    [Fact]
    public void RecordFailure_FailsClosed_NoStateCommitted_NoPhysicalFallback()
    {
        var config = ValidConfig();
        var attempts = 0;
        using var sink = new DryRunGpioController(config, _ =>
        {
            attempts++;
            throw new InvalidOperationException("synthetic record sink failure");
        });

        var thrown = Assert.Throws<InvalidOperationException>(
            () => sink.ConfigureOutput(config.Pin1));
        Assert.Equal("synthetic record sink failure", thrown.Message);
        Assert.Equal(1, attempts);

        // The failed configure committed nothing: the line is still not an
        // output, so the follow-up write is rejected by validation — and that
        // rejection record fails closed as well.
        Assert.Throws<InvalidOperationException>(
            () => sink.Write(config.Pin1, GpioPinValue.High));
        Assert.Equal(2, attempts);

        // There is no real sink in this object graph to fall through to.
    }

    [Fact]
    public void DisposedSink_IsDeterministicStateFailure_WithoutRecords()
    {
        var records = new List<DryRunOperationRecord>();
        var config = ValidConfig();
        var sink = new DryRunGpioController(config, records.Add);
        sink.ConfigureOutput(config.Pin1);
        sink.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sink.Write(config.Pin1, GpioPinValue.High));
        Assert.Throws<ObjectDisposedException>(() => sink.ConfigureOutput(config.Pin2));
        Assert.Single(records); // only the pre-dispose configure
    }

    [Fact]
    public async Task SafetyFlow_RecordsEveryIntendedOperationAsSuppressed_IncludingStop()
    {
        var records = new List<DryRunOperationRecord>();
        var config = ValidConfig();
        var realPlatform = new RecordingRealPlatform();
        var sink = new DryRunGpioController(config, records.Add);
        var hardware = new FakeOutputProducerHardware(sink, config);
        var inner = new HardwareDroneController(hardware);
        var safety = new DroneSafetyController(
            inner,
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 120,
                stopTimeoutMilliseconds: 500,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

        // Startup baseline (connect → STOP → disconnect) through Task 26.
        await safety.StartSafetyAsync();
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        var afterBaseline = records.Count;
        Assert.True(afterBaseline > 0);

        // Connection baseline + a semantic movement command.
        await safety.ConnectAsync();
        var beforeMovement = records.Count;
        await safety.SendCommandAsync(DroneCommand.Forward);
        Assert.Equal(DroneSafetyState.CommandActive, safety.CurrentSafetyState);
        Assert.True(records.Count > beforeMovement); // command's intended ops recorded synchronously

        // Movement liveness timeout asks for STOP through the same path; the
        // intended STOP operations are recorded and suppressed, not omitted.
        // (Baseline stop: 4 records; movement: 4; liveness stop: 4.)
        await WaitForStateAsync(safety, DroneSafetyState.Safe);
        Assert.True(records.Count >= beforeMovement + 8);
        Assert.Equal(DroneCommand.Stop, (await inner.GetStatusAsync()).State.ConfirmedCommand);

        // Explicit STOP through the same supervised path.
        var beforeExplicitStop = records.Count;
        await safety.SendCommandAsync(DroneCommand.Stop);
        Assert.True(records.Count > beforeExplicitStop);

        // Speed validation keeps real-mode parity and stays observable.
        await safety.SetSpeedAsync(50);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => safety.SetSpeedAsync(101));

        // Structurally no physical sink exists in this graph, and the real
        // platform double recorded zero operations.
        Assert.Empty(realPlatform.Calls);
        Assert.All(records, record =>
        {
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
            Assert.Equal(HardwareOutputCategory.Gpio, record.Category);
        });

        sink.Dispose();
    }

    [Fact]
    public async Task RecordFailure_DuringSafetyFlow_LeavesSafetyPathFaulted_PerTask26()
    {
        var config = ValidConfig();
        var sink = new DryRunGpioController(config, _ =>
            throw new InvalidOperationException("synthetic record sink failure"));
        var hardware = new FakeOutputProducerHardware(sink, config);
        var inner = new HardwareDroneController(hardware);
        var safety = new DroneSafetyController(
            inner,
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 200,
                stopTimeoutMilliseconds: 100,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

        // Startup baseline cannot record the intended STOP → fail closed into
        // the Task 26 faulted state; no silent fallback and no mock path.
        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => safety.SendCommandAsync(DroneCommand.Forward));
        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);

        sink.Dispose();
    }

    [Fact]
    public async Task CancelledCallerToken_ProducesNoOperations_AndLeavesStateUnchanged()
    {
        var records = new List<DryRunOperationRecord>();
        var config = ValidConfig();
        var sink = new DryRunGpioController(config, records.Add);
        var hardware = new FakeOutputProducerHardware(sink, config);
        var inner = new HardwareDroneController(hardware);
        var safety = new DroneSafetyController(
            inner,
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 500,
                stopTimeoutMilliseconds: 100,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

        await safety.StartSafetyAsync();
        await safety.ConnectAsync();
        var countBefore = records.Count;

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => safety.SendCommandAsync(DroneCommand.Forward, cancelled.Token));

        Assert.Equal(countBefore, records.Count);
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);

        sink.Dispose();
    }
}
