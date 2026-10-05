using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// PC-only tests for the Task 30 SpeedController
/// (specs/hardware/pwm.md). Frequency and duty values are a SYNTHETIC TEST
/// FIXTURE — deliberately arbitrary test data that encodes NO physical or
/// electrical meaning, never to be reused as hardware data. These tests prove
/// software mapping, ordering, tracking, and shutdown mechanics only; they
/// cannot prove that any duty stops or drives a motor (physical confirmation
/// is deferred and NOT VERIFIED).
/// </summary>
public class SpeedControllerTests
{
    /// <summary>Test-only recording sink; performs no hardware work.</summary>
    private sealed class RecordingPwmSink : IPwmController
    {
        public List<string> Calls { get; } = [];

        /// <summary>1-based call number that throws instead of completing.</summary>
        public int FailAtCall { get; set; } = -1;

        public void ConfigureOutput(int identifier, int frequencyHz)
        {
            Calls.Add($"configure:{identifier}:{frequencyHz}");
            ThrowIfFailing();
        }

        public void SetDutyCycle(int identifier, int dutyPercent)
        {
            Calls.Add($"duty:{identifier}:{dutyPercent}");
            ThrowIfFailing();
        }

        public void Dispose()
        {
        }

        private void ThrowIfFailing()
        {
            if (Calls.Count == FailAtCall)
            {
                throw new InvalidOperationException("synthetic sink failure");
            }
        }
    }

    private static Dictionary<string, string?> GpioValues() => new()
    {
        ["Pin1"] = "23",
        ["Pin2"] = "24",
        ["Pin3"] = "21",
        ["Pin4"] = "20",
        ["PWM1"] = "12",
        ["PWM2"] = "13",
    };

    private static PwmMapping Mapping(int min = 20, int max = 80, int frequency = 8000)
    {
        var mapping = PwmMapping.FromSection(
            new Dictionary<string, string?>
            {
                ["FrequencyHz"] = frequency.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MinDutyPercent"] = min.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MaxDutyPercent"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SpeedMappingVerified"] = "true",
            },
            GpioPinConfiguration.FromSection(GpioValues()),
            GpioValues());
        return Assert.IsType<PwmMapping>(mapping);
    }

    private static (SpeedController Controller, RecordingPwmSink Sink) Create(
        int min = 20, int max = 80, int frequency = 8000)
    {
        var sink = new RecordingPwmSink();
        return (new SpeedController(sink, Mapping(min, max, frequency)), sink);
    }

    // --- Duty formula: every boundary test-pinned by the specification ---

    [Theory]
    [InlineData(0, 20, 80, 0)]
    [InlineData(1, 20, 80, 20)]
    [InlineData(33, 20, 80, 39)]
    [InlineData(100, 20, 80, 80)]
    [InlineData(1, 0, 100, 1)]
    [InlineData(50, 10, 10, 10)]
    [InlineData(50, 0, 0, 0)]
    [InlineData(100, 0, 0, 0)]
    public void DutyFormula_MatchesSpecificationExactly(
        int speed, int min, int max, int expectedDuty)
    {
        var (controller, sink) = Create(min, max);

        controller.Apply(speed);

        Assert.Equal(
            new[]
            {
                "configure:12:8000", "configure:13:8000",
                $"duty:12:{expectedDuty}", $"duty:13:{expectedDuty}",
            },
            sink.Calls);
        Assert.Equal(speed, controller.LastRequestedSpeedPercent);
        Assert.Equal(expectedDuty, controller.LastAppliedDutyPercent);
    }

    [Fact]
    public void Apply_SpeedZero_IssuesTheRealDutyZeroSequence_NeverSkipped()
    {
        var (controller, sink) = Create();

        controller.Apply(0);

        Assert.Equal(
            new[]
            {
                "configure:12:8000", "configure:13:8000",
                "duty:12:0", "duty:13:0",
            },
            sink.Calls);
        Assert.Equal(0, controller.LastRequestedSpeedPercent);
        Assert.Equal(0, controller.LastAppliedDutyPercent);
    }

    [Fact]
    public void Apply_ConfiguresAllIdentifiersFirst_ThenWritesDuties_Pwm1BeforePwm2()
    {
        var (controller, sink) = Create();

        controller.Apply(45);

        // Four operations per apply: configure x2 (PWM1, PWM2), duty x2
        // (PWM1, PWM2) — configure-before-write, deterministic identifier
        // order, exactly one duty computation reused for both.
        Assert.Equal(
            new[]
            {
                "configure:12:8000", "configure:13:8000",
                "duty:12:47", "duty:13:47",
            },
            sink.Calls);
    }

    // --- Range and cancellation invariants ---

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Apply_OutOfRangeSpeed_Throws_BeforeAnySinkOperation(int speed)
    {
        var (controller, sink) = Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Apply(speed));

        Assert.Empty(sink.Calls);
        Assert.Null(controller.LastRequestedSpeedPercent);
        Assert.Null(controller.LastAppliedDutyPercent);
    }

    [Fact]
    public void Apply_CancelledToken_CancelsBeforeAnySinkOperation()
    {
        var (controller, sink) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => controller.Apply(50, cts.Token));

        Assert.Empty(sink.Calls);
        Assert.Null(controller.LastRequestedSpeedPercent);
    }

    // --- Fail-closed application and tracking ---

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Apply_SinkFailureOnAnyOperation_Propagates_WithNoFurtherOperations(int failAt)
    {
        var (controller, sink) = Create();
        sink.FailAtCall = failAt;

        Assert.Throws<InvalidOperationException>(() => controller.Apply(45));

        // The failing operation is the last attempted one: nothing after it.
        Assert.Equal(failAt, sink.Calls.Count);
        Assert.Null(controller.LastRequestedSpeedPercent);
        Assert.Null(controller.LastAppliedDutyPercent);
    }

    [Fact]
    public void Apply_SinkFailure_LeavesTrackingAtPreviousValues()
    {
        var (controller, sink) = Create();
        controller.Apply(45);
        Assert.Equal(45, controller.LastRequestedSpeedPercent);
        Assert.Equal(47, controller.LastAppliedDutyPercent);

        sink.FailAtCall = sink.Calls.Count + 3; // second apply's first duty write
        Assert.Throws<InvalidOperationException>(() => controller.Apply(90));

        Assert.Equal(45, controller.LastRequestedSpeedPercent);
        Assert.Equal(47, controller.LastAppliedDutyPercent);
    }

    [Fact]
    public void Tracking_UpdatesOnlyAfterCompleteSuccess_AcrossRepeatedApplies()
    {
        var (controller, sink) = Create();

        // Null until the first success.
        Assert.Null(controller.LastRequestedSpeedPercent);
        Assert.Null(controller.LastAppliedDutyPercent);

        controller.Apply(10);
        Assert.Equal(10, controller.LastRequestedSpeedPercent);
        Assert.Equal(26, controller.LastAppliedDutyPercent);

        controller.Apply(90);
        Assert.Equal(90, controller.LastRequestedSpeedPercent);
        Assert.Equal(74, controller.LastAppliedDutyPercent);

        // The full deterministic sequence repeats per apply (8 operations).
        Assert.Equal(
            new[]
            {
                "configure:12:8000", "configure:13:8000", "duty:12:26", "duty:13:26",
                "configure:12:8000", "configure:13:8000", "duty:12:74", "duty:13:74",
            },
            sink.Calls);
    }

    // --- Shutdown behavior ---

    [Fact]
    public void Dispose_AfterNonZeroDuty_IssuesTheDutyZeroSequence()
    {
        var (controller, sink) = Create();
        controller.Apply(45);
        var before = sink.Calls.Count;

        controller.Dispose();

        Assert.Equal(
            new[]
            {
                "configure:12:8000", "configure:13:8000",
                "duty:12:0", "duty:13:0",
            },
            sink.Calls.Skip(before).ToArray());

        // Idempotent: a second disposal issues nothing.
        var afterDisposal = sink.Calls.Count;
        controller.Dispose();
        Assert.Equal(afterDisposal, sink.Calls.Count);
    }

    [Fact]
    public void Dispose_DutyZeroSequence_IsAssertedByDryRunRecords()
    {
        var records = new List<DryRunOperationRecord>();
        using var dryRunSink = new DryRunPwmController([12, 13], records.Add);
        var controller = new SpeedController(dryRunSink, Mapping());
        controller.Apply(45);
        records.Clear();

        controller.Dispose();

        Assert.Equal(
            new[]
            {
                "configure frequency-hz=8000", "configure frequency-hz=8000",
                "write duty-percent=0", "write duty-percent=0",
            },
            records.Select(record => record.RequestedParameters).ToArray());
        Assert.All(records, record =>
        {
            Assert.Equal(HardwareOutputCategory.Pwm, record.Category);
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
        });
    }

    [Fact]
    public void Dispose_WithoutAnyApplication_IssuesNothing()
    {
        var (controller, sink) = Create();

        controller.Dispose();

        Assert.Empty(sink.Calls);
    }

    [Fact]
    public void Dispose_AfterDutyZeroAlreadyApplied_IssuesNothing()
    {
        var (controller, sink) = Create();
        controller.Apply(0);
        var before = sink.Calls.Count;

        controller.Dispose();

        Assert.Equal(before, sink.Calls.Count);
    }

    [Fact]
    public void Dispose_FailurePropagates_NeverSwallowedIntoSuccess()
    {
        var (controller, sink) = Create();
        controller.Apply(45);
        sink.FailAtCall = sink.Calls.Count + 1;

        // Disposal failure surfaces to the caller (host disposal logging);
        // it is never converted into a silent success.
        Assert.Throws<InvalidOperationException>(() => controller.Dispose());
    }

    [Fact]
    public void Apply_AfterDispose_ThrowsObjectDisposed_WithoutTouchingTheSink()
    {
        var (controller, sink) = Create();
        controller.Apply(45);
        controller.Dispose();
        var before = sink.Calls.Count;

        Assert.Throws<ObjectDisposedException>(() => controller.Apply(50));

        Assert.Equal(before, sink.Calls.Count);
    }

    // --- Constructor contracts ---

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SpeedController(null!, Mapping()));
        Assert.Throws<ArgumentNullException>(
            () => new SpeedController(new RecordingPwmSink(), null!));
    }
}
