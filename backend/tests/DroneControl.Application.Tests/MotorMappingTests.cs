using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// PC-only tests for the Task 29 motor mapping and controller
/// (specs/hardware/motor-control.md). The level values below are a SYNTHETIC
/// TEST FIXTURE: deliberately arbitrary test data that encodes NO physical,
/// wiring, L298N, or motor meaning — never reuse them as hardware data.
/// These tests prove software mechanics only; they cannot prove that any
/// combination moves a motor in a particular direction (physical confirmation
/// is deferred and NOT VERIFIED).
/// </summary>
public class MotorMappingTests
{
    private static readonly Dictionary<string, string?> SyntheticSection = new()
    {
        ["DirectionMappingVerified"] = "true",
        ["Commands:forward:Pin1"] = "High",
        ["Commands:forward:Pin2"] = "Low",
        ["Commands:forward:Pin3"] = "High",
        ["Commands:forward:Pin4"] = "Low",
        ["Commands:backward:Pin1"] = "Low",
        ["Commands:backward:Pin2"] = "High",
        ["Commands:backward:Pin3"] = "Low",
        ["Commands:backward:Pin4"] = "High",
        ["Commands:left:Pin1"] = "High",
        ["Commands:left:Pin2"] = "High",
        ["Commands:left:Pin3"] = "Low",
        ["Commands:left:Pin4"] = "Low",
        ["Commands:right:Pin1"] = "Low",
        ["Commands:right:Pin2"] = "Low",
        ["Commands:right:Pin3"] = "High",
        ["Commands:right:Pin4"] = "High",
        ["Commands:stop:Pin1"] = "Low",
        ["Commands:stop:Pin2"] = "Low",
        ["Commands:stop:Pin3"] = "Low",
        ["Commands:stop:Pin4"] = "Low",
    };

    private static Dictionary<string, string?> Section(bool verified = true)
    {
        var copy = new Dictionary<string, string?>(SyntheticSection);
        copy["DirectionMappingVerified"] = verified ? "true" : "false";
        return copy;
    }

    private static GpioPinConfiguration Gpio()
        => GpioPinConfiguration.FromSection(new Dictionary<string, string?>
        {
            ["Pin1"] = "23",
            ["Pin2"] = "24",
            ["Pin3"] = "21",
            ["Pin4"] = "20",
            ["PWM1"] = "12",
            ["PWM2"] = "13",
        });

    private static string[] ExpectedApplySequence(IReadOnlyDictionary<string, string?> section, string command)
        => [.. new[] { 23, 24, 21, 20 }.Select(pin => $"configure:{pin}"),
            .. new[] { 23, 24, 21, 20 }.Select(pin => $"write:{pin}:{section[$"Commands:{command}:Pin{pin switch { 23 => 1, 24 => 2, 21 => 3, _ => 4 }}"]}")];

    /// <summary>Test-only recording sink; performs no hardware work.</summary>
    private sealed class RecordingSink : IGpioController
    {
        public List<string> Calls { get; } = [];

        public void ConfigureOutput(int pin) => Calls.Add($"configure:{pin}");

        public void Write(int pin, GpioPinValue value) => Calls.Add($"write:{pin}:{value}");

        public void Dispose()
        {
        }
    }

    /// <summary>Test-only sink that fails on its first write (mid-sequence).</summary>
    private sealed class FailingWriteSink : IGpioController
    {
        public List<string> Calls { get; } = [];

        public void ConfigureOutput(int pin) => Calls.Add($"configure:{pin}");

        public void Write(int pin, GpioPinValue value)
        {
            Calls.Add($"write:{pin}:{value}");
            throw new InvalidOperationException("synthetic sink write failure");
        }

        public void Dispose()
        {
        }
    }

    // --- MotorMapping.FromSection: valid shapes ---

    [Fact]
    public void ValidSection_BindsAllFiveCommands_FullCoverage_DeterministicOrder()
    {
        var mapping = MotorMapping.FromSection(Section(), Gpio());

        Assert.True(mapping.DirectionMappingVerified);
        Assert.Equal(new[] { 23, 24, 21, 20 }, mapping.LineIdentifiers);
        Assert.Equal(Enum.GetValues<DroneCommand>(), mapping.Commands.Keys.OrderBy(key => key));
        Assert.All(mapping.Commands.Values, levels => Assert.Equal(4, levels.Count));
    }

    [Fact]
    public void UnverifiedFlag_Binds_ButStaysInactiveMaterial()
    {
        var mapping = MotorMapping.FromSection(Section(verified: false), Gpio());

        // Valid but unasserted: the composition root treats this as inactive
        // (matrix row "present, valid | false" → identical legacy behavior).
        Assert.False(mapping.DirectionMappingVerified);
        Assert.Equal(5, mapping.Commands.Count);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("  true  ")]
    [InlineData("False")]
    public void FlagTokens_AreBooleanAndTrimmed(string token)
    {
        var section = Section();
        section["DirectionMappingVerified"] = token;

        var mapping = MotorMapping.FromSection(section, Gpio());

        Assert.Equal(bool.Parse(token.Trim()), mapping.DirectionMappingVerified);
    }

    [Fact]
    public void CommandAndLevelTokens_AreCaseInsensitiveAndTrimmed()
    {
        var section = new Dictionary<string, string?>
        {
            ["directionmappingverified"] = "True",
            ["Commands:FORWARD:Pin1"] = " high ",
            ["Commands:FORWARD:Pin2"] = "LOW",
            ["Commands:FORWARD:Pin3"] = "High",
            ["Commands:FORWARD:Pin4"] = "Low",
            ["Commands:backward:Pin1"] = "Low",
            ["Commands:backward:Pin2"] = "High",
            ["Commands:backward:Pin3"] = "Low",
            ["Commands:backward:Pin4"] = "High",
            ["Commands:left:Pin1"] = "High",
            ["Commands:left:Pin2"] = "High",
            ["Commands:left:Pin3"] = "Low",
            ["Commands:left:Pin4"] = "Low",
            ["Commands:right:Pin1"] = "Low",
            ["Commands:right:Pin2"] = "Low",
            ["Commands:right:Pin3"] = "High",
            ["Commands:right:Pin4"] = "High",
            ["Commands:stop:Pin1"] = "Low",
            ["Commands:stop:Pin2"] = "Low",
            ["Commands:stop:Pin3"] = "Low",
            ["Commands:stop:Pin4"] = "Low",
        };

        var mapping = MotorMapping.FromSection(section, Gpio());

        Assert.True(mapping.DirectionMappingVerified);
        Assert.Equal(GpioPinValue.High, mapping.Commands[DroneCommand.Forward][23]);
        Assert.Equal(GpioPinValue.Low, mapping.Commands[DroneCommand.Forward][24]);
    }

    // --- MotorMapping.FromSection: invalid shapes (all enumerated) ---

    [Fact]
    public void MissingStopCommand_IsRejected_Explicitly()
    {
        var section = Section();
        foreach (var key in section.Keys.Where(key => key.StartsWith("Commands:stop:")).ToArray())
        {
            section.Remove(key);
        }

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("'stop' is missing", thrown.Message);
    }

    [Fact]
    public void MissingCommand_IsRejected_Explicitly()
    {
        var section = Section();
        foreach (var key in section.Keys.Where(key => key.StartsWith("Commands:left:")).ToArray())
        {
            section.Remove(key);
        }

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("'left' is missing", thrown.Message);
    }

    [Fact]
    public void UnknownCommand_IsRejected_Explicitly()
    {
        var section = Section();
        section["Commands:hover:Pin1"] = "High";

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("Unknown motor mapping command 'hover'", thrown.Message);
        Assert.Contains("stop", thrown.Message);
    }

    [Fact]
    public void IncompleteIdentifierCoverage_IsRejected_NamingTheIdentifier()
    {
        var section = Section();
        section.Remove("Commands:forward:Pin3");

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("'forward' is missing a level for identifier 'Pin3'", thrown.Message);
    }

    [Theory]
    [InlineData("Medium")]
    [InlineData("2")]
    [InlineData("")]
    public void NonHighLowLevelToken_IsRejected(string token)
    {
        var section = Section();
        section["Commands:forward:Pin2"] = token;

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("must be HIGH or LOW", thrown.Message);
    }

    [Theory]
    [InlineData("Pin5")]
    [InlineData("PWM1")]
    [InlineData("pwm2")]
    public void UnknownIdentifier_IsRejected_ByName(string identifier)
    {
        var section = Section();
        section[$"Commands:forward:{identifier}"] = "High";

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("is not accepted", thrown.Message);
        Assert.Contains("PwmMapping", thrown.Message);
    }

    [Fact]
    public void MalformedFlag_IsRejected()
    {
        var section = Section();
        section["DirectionMappingVerified"] = "yes";

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("DirectionMappingVerified must be 'true' or 'false'", thrown.Message);
    }

    [Fact]
    public void UnknownTopLevelEntry_IsRejected()
    {
        var section = Section();
        section["Speed"] = "50";

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("Unknown MotorMapping entry 'Speed'", thrown.Message);
    }

    [Fact]
    public void EmptyCommandsNode_IsRejected()
    {
        var section = new Dictionary<string, string?>
        {
            ["DirectionMappingVerified"] = "false",
            ["Commands"] = null,
        };

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("'Commands' is present but contains no command entries", thrown.Message);
        Assert.Contains("'forward' is missing", thrown.Message);
    }

    [Fact]
    public void AssertionWithoutMappingData_IsRejected_AsContradiction()
    {
        var section = new Dictionary<string, string?>
        {
            ["DirectionMappingVerified"] = "true",
        };

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));
        Assert.Contains("asserts a verified mapping, but no Commands mapping data is present", thrown.Message);
        Assert.Contains("'stop' is missing", thrown.Message);
    }

    [Fact]
    public void MultipleProblems_AreAllEnumeratedInASingleFailure()
    {
        var section = Section();
        foreach (var key in section.Keys.Where(key => key.StartsWith("Commands:stop:")).ToArray())
        {
            section.Remove(key);
        }

        section["Commands:forward:Pin2"] = "Medium";
        section["Speed"] = "50";

        var thrown = Assert.Throws<ArgumentException>(
            () => MotorMapping.FromSection(section, Gpio()));

        Assert.Contains("3 problem(s)", thrown.Message);
        Assert.Contains("'stop' is missing", thrown.Message);
        Assert.Contains("must be HIGH or LOW", thrown.Message);
        Assert.Contains("Unknown MotorMapping entry 'Speed'", thrown.Message);
    }

    [Fact]
    public void NullArguments_AreRejected_BeforeValidation()
    {
        Assert.Throws<ArgumentNullException>(
            () => MotorMapping.FromSection(null!, Gpio()));
        Assert.Throws<ArgumentNullException>(
            () => MotorMapping.FromSection(Section(), null!));
    }

    // --- MotorController: active application ---

    [Theory]
    [InlineData("forward")]
    [InlineData("backward")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("stop")]
    public void Apply_ConfiguresEveryLineFirst_ThenWritesInDeterministicOrder(string command)
    {
        var section = Section();
        var mapping = MotorMapping.FromSection(section, Gpio());
        var sink = new RecordingSink();
        var controller = new MotorController(sink, mapping);

        controller.Apply(Enum.Parse<DroneCommand>(command, ignoreCase: true));

        Assert.Equal(ExpectedApplySequence(section, command), sink.Calls);
    }

    [Fact]
    public void Apply_RepeatedCalls_ProduceTheIdenticalSequence()
    {
        var mapping = MotorMapping.FromSection(Section(), Gpio());
        var sink = new RecordingSink();
        var controller = new MotorController(sink, mapping);

        controller.Apply(DroneCommand.Left);
        var firstPass = sink.Calls.ToArray();
        sink.Calls.Clear();
        controller.Apply(DroneCommand.Left);

        Assert.Equal(firstPass, sink.Calls);
    }

    [Fact]
    public void Apply_UndefinedCommand_ThrowsBeforeTouchingTheSink()
    {
        var mapping = MotorMapping.FromSection(Section(), Gpio());
        var sink = new RecordingSink();
        var controller = new MotorController(sink, mapping);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => controller.Apply((DroneCommand)42));

        Assert.Empty(sink.Calls);
    }

    [Fact]
    public void Apply_NullArguments_AreRejected()
    {
        var mapping = MotorMapping.FromSection(Section(), Gpio());
        Assert.Throws<ArgumentNullException>(() => new MotorController(null!, mapping));
        Assert.Throws<ArgumentNullException>(() => new MotorController(new RecordingSink(), null!));
    }

    // --- Active provider (RaspberryDroneHardware with a MotorController) ---

    [Fact]
    public async Task ActiveProvider_ReportsAvailable_AndAppliesThroughTheMapping()
    {
        var sink = new RecordingSink();
        var controller = new MotorController(
            sink, MotorMapping.FromSection(Section(), Gpio()));
        IDroneHardware provider = new RaspberryDroneHardware(controller);

        Assert.Equal(HardwareAvailability.Available, await provider.GetAvailabilityAsync());
        var result = await provider.ExecuteCommandAsync(DroneCommand.Forward);

        Assert.Equal(HardwareCommandStatus.Applied, result.Status);

        // Applied only after every operation completed: the full sequence is
        // already on the sink when the result is observed.
        Assert.Equal(8, sink.Calls.Count);
        Assert.Equal("write:20:Low", sink.Calls[^1]);
    }

    [Fact]
    public async Task ActiveProvider_SinkFailureMidCommand_Propagates_NoResult_NoRetry()
    {
        var sink = new FailingWriteSink();
        var controller = new MotorController(
            sink, MotorMapping.FromSection(Section(), Gpio()));
        IDroneHardware provider = new RaspberryDroneHardware(controller);

        // Sequential, non-atomic by design: partial operations may exist, but
        // no result is produced and nothing is retried at this layer.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ExecuteCommandAsync(DroneCommand.Forward));
        Assert.Equal("synthetic sink write failure", thrown.Message);
        Assert.Equal(5, sink.Calls.Count); // 4 configures + the failing write
    }

    [Fact]
    public async Task ActiveProvider_CancelledToken_ProducesNoOperations()
    {
        var sink = new RecordingSink();
        var controller = new MotorController(
            sink, MotorMapping.FromSection(Section(), Gpio()));
        IDroneHardware provider = new RaspberryDroneHardware(controller);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExecuteCommandAsync(DroneCommand.Forward, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAvailabilityAsync(cancelled.Token));

        Assert.Empty(sink.Calls);
    }

    [Fact]
    public async Task ActiveProvider_Speed_IsExplicitlyUnsupported_NeverSilentlySuccessful()
    {
        var sink = new RecordingSink();
        var controller = new MotorController(
            sink, MotorMapping.FromSection(Section(), Gpio()));
        IDroneHardware provider = new RaspberryDroneHardware(controller);

        var thrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => provider.ApplySpeedAsync(45));
        Assert.Contains("PwmMapping", thrown.Message);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.ApplySpeedAsync(101));

        // Nothing about speed reaches the direction sink: this provider has
        // no SpeedController, so no PWM operation can exist.
        Assert.Empty(sink.Calls);
    }

    [Fact]
    public async Task InactiveProvider_StaysUnavailable_EvenWhenARecordingSinkExists()
    {
        var sink = new RecordingSink();
        IDroneHardware provider = new RaspberryDroneHardware();

        Assert.Equal(HardwareAvailability.Unavailable, await provider.GetAvailabilityAsync());
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => provider.ExecuteCommandAsync(DroneCommand.Forward));
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => provider.ApplySpeedAsync(45));

        Assert.Empty(sink.Calls);
    }

    // --- Task 26 safety path over the active provider ---

    [Fact]
    public async Task SafetyPath_SinkFailureAtBaseline_LeavesSafetyFaulted_PerTask26()
    {
        var sink = new FailingWriteSink();
        var provider = new RaspberryDroneHardware(
            new MotorController(sink, MotorMapping.FromSection(Section(), Gpio())));
        var safety = new DroneSafetyController(
            new HardwareDroneController(provider),
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 200,
                stopTimeoutMilliseconds: 100,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

        // The startup baseline STOP fails mid-command at the sink; Task 26
        // applies its existing transition — faulted, movement blocked.
        await safety.StartSafetyAsync();

        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => safety.SendCommandAsync(DroneCommand.Forward));
        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);

        // Blocked movement never reached the sink (baseline attempt only:
        // 4 configures + the single failed write).
        Assert.Equal(5, sink.Calls.Count);
    }

    [Fact]
    public async Task SafetyPath_ActiveProvider_BaselineAndStop_UseTheStopEntry_ThroughTheSamePath()
    {
        var sink = new RecordingSink();
        var provider = new RaspberryDroneHardware(
            new MotorController(sink, MotorMapping.FromSection(Section(), Gpio())));
        var safety = new DroneSafetyController(
            new HardwareDroneController(provider),
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 30_000,
                stopTimeoutMilliseconds: 1_000,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

        string[] stopSequence =
        [
            "configure:23", "configure:24", "configure:21", "configure:20",
            "write:23:Low", "write:24:Low", "write:21:Low", "write:20:Low",
        ];
        string[] forwardSequence =
        [
            "configure:23", "configure:24", "configure:21", "configure:20",
            "write:23:High", "write:24:Low", "write:21:High", "write:20:Low",
        ];

        // Startup baseline: connect → STOP (the mapping's stop entry) → disconnect.
        await safety.StartSafetyAsync();
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal(stopSequence, sink.Calls);

        // Connection baseline issues the stop entry again through the same path.
        await safety.ConnectAsync();
        Assert.Equal(stopSequence.Concat(stopSequence), sink.Calls);

        // A movement command applies its own entry; STOP afterwards returns to
        // the stop entry and Safe — no special casing anywhere.
        await safety.SendCommandAsync(DroneCommand.Forward);
        Assert.Equal(DroneSafetyState.CommandActive, safety.CurrentSafetyState);
        Assert.Equal(stopSequence.Concat(stopSequence).Concat(forwardSequence), sink.Calls);

        await safety.SendCommandAsync(DroneCommand.Stop);
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        Assert.Equal(
            stopSequence.Concat(stopSequence).Concat(forwardSequence).Concat(stopSequence),
            sink.Calls);
        var status = await safety.GetStatusAsync();
        Assert.Equal(DroneCommand.Stop, status.State.ConfirmedCommand);
    }

    // --- Dry-run integration: same upstream sequence, every record suppressed ---

    [Fact]
    public void DryRunSink_ActiveMapping_SameSequence_EveryRecordSuppressed()
    {
        var section = Section();
        var mapping = MotorMapping.FromSection(section, Gpio());
        var config = Gpio();
        var records = new List<DryRunOperationRecord>();
        using var drySink = new DryRunGpioController(config, records.Add);
        var recordingSink = new RecordingSink();

        var dryController = new MotorController(drySink, mapping);
        var realPathController = new MotorController(recordingSink, mapping);
        dryController.Apply(DroneCommand.Forward);
        realPathController.Apply(DroneCommand.Forward);

        // Same upstream sequence; only the final sink differs.
        var reconstructed = records.Select(record => record.RequestedParameters == "configure-output"
            ? $"configure:{record.Identifier}"
            : $"write:{record.Identifier}:{record.RequestedParameters["write value=".Length..]}").ToArray();
        Assert.Equal(recordingSink.Calls, reconstructed);

        Assert.Equal(8, records.Count);
        Assert.Equal(new[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L },
            records.Select(record => record.Sequence).ToArray());
        Assert.All(records, record =>
        {
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
            Assert.Equal(HardwareOutputCategory.Gpio, record.Category);
            Assert.Contains("suppress", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confirm", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
        });
    }
}
