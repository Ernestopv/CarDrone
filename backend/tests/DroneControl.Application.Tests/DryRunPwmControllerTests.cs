using DroneControl.Application;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// PC-only dry-run PWM tests (specs/hardware/pwm.md,
/// specs/hardware/dry-run.md). They prove record invariants, rejection
/// parity with the GPIO boundary, and fail-closed suppression using fakes.
/// They do NOT prove that a real Pi cannot energize a motor, that duty 0
/// stops anything, or any PWM electrical behavior.
/// </summary>
public class DryRunPwmControllerTests
{
    private static IReadOnlyCollection<int> Identifiers => [12, 13];

    private sealed class Recorder
    {
        public List<DryRunOperationRecord> Records { get; } = [];
        public int FailAtInvocation { get; set; } = -1;
        public int Invocations { get; private set; }

        public void Callback(DryRunOperationRecord record)
        {
            Invocations++;
            if (Invocations == FailAtInvocation)
            {
                throw new InvalidOperationException("synthetic record failure");
            }

            Records.Add(record);
        }
    }

    private static (DryRunPwmController Sink, Recorder Recorder) Create()
    {
        var recorder = new Recorder();
        return (new DryRunPwmController(Identifiers, recorder.Callback), recorder);
    }

    // --- Valid operations: recorded, suppressed, in order ---

    [Fact]
    public void ValidOperations_AreRecordedSuppressed_WithTheFixedFormats()
    {
        var (sink, recorder) = Create();

        sink.ConfigureOutput(12, 8000);
        sink.ConfigureOutput(13, 8000);
        sink.SetDutyCycle(12, 50);
        sink.SetDutyCycle(13, 50);

        Assert.Equal(4, recorder.Records.Count);
        Assert.Equal(
            new[] { "configure frequency-hz=8000", "configure frequency-hz=8000", "write duty-percent=50", "write duty-percent=50" },
            recorder.Records.Select(record => record.RequestedParameters).ToArray());
        Assert.Equal(new[] { "12", "13", "12", "13" },
            recorder.Records.Select(record => record.Identifier).ToArray());
        Assert.Equal(new[] { 1L, 2L, 3L, 4L },
            recorder.Records.Select(record => record.Sequence).ToArray());
        Assert.All(recorder.Records, record =>
        {
            Assert.Equal(HardwareOutputCategory.Pwm, record.Category);
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
            Assert.Contains("suppress", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
            // A record is intent, never confirmation or acknowledgement.
            Assert.DoesNotContain("confirm", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("acknowledg", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
        });
    }

    // --- Rejections: record + matching exception, GPIO parity ---

    [Theory]
    [InlineData("unknown-identifier")]
    [InlineData("bad-frequency")]
    [InlineData("duty-above-100")]
    [InlineData("duty-below-0")]
    [InlineData("write-before-configure")]
    public void RejectedOperations_RecordTheRejection_ThenThrow(string rejection)
    {
        var (sink, recorder) = Create();
        if (rejection == "write-before-configure")
        {
            sink.ConfigureOutput(12, 8000); // the valid preceding operation
        }

        var expectedCalls = recorder.Records.Count + 1;
        switch (rejection)
        {
            case "unknown-identifier":
                Assert.Throws<ArgumentException>(() => sink.ConfigureOutput(99, 8000));
                break;
            case "bad-frequency":
                Assert.Throws<ArgumentOutOfRangeException>(() => sink.ConfigureOutput(12, 0));
                break;
            case "duty-above-100":
                Assert.Throws<ArgumentOutOfRangeException>(() => sink.SetDutyCycle(12, 101));
                break;
            case "duty-below-0":
                Assert.Throws<ArgumentOutOfRangeException>(() => sink.SetDutyCycle(13, -1));
                break;
            case "write-before-configure":
                Assert.Throws<InvalidOperationException>(() => sink.SetDutyCycle(13, 50));
                break;
        }

        Assert.Equal(expectedCalls, recorder.Records.Count);
        var record = recorder.Records[^1];
        Assert.Equal(HardwareOutputCategory.Pwm, record.Category);
        Assert.Equal(DryRunPwmController.RejectedOperationReason, record.SuppressionReason);
        Assert.True(record.DryRun);
        Assert.False(record.PhysicallyApplied);
    }

    [Fact]
    public void UnknownIdentifierDutyWrite_IsRejected_BeforeTheConfiguredOutputCheck()
    {
        var (sink, recorder) = Create();
        sink.ConfigureOutput(12, 8000);

        Assert.Throws<ArgumentException>(() => sink.SetDutyCycle(99, 50));

        var record = recorder.Records[^1];
        Assert.Equal(DryRunPwmController.RejectedOperationReason, record.SuppressionReason);
        Assert.Equal("99", record.Identifier);
        Assert.Equal("write duty-percent=50", record.RequestedParameters);
    }

    [Fact]
    public void Disposed_ThrowsObjectDisposed_WithoutNewRecords()
    {
        var (sink, recorder) = Create();
        sink.ConfigureOutput(12, 8000);
        sink.Dispose();
        var before = recorder.Records.Count;

        Assert.Throws<ObjectDisposedException>(() => sink.ConfigureOutput(13, 8000));
        Assert.Throws<ObjectDisposedException>(() => sink.SetDutyCycle(12, 50));

        Assert.Equal(before, recorder.Records.Count);
    }

    // --- Fail-closed: a record failure never commits operation state ---

    [Fact]
    public void RecordCallbackFailure_Propagates_AndCommitsNothing()
    {
        var recorder = new Recorder { FailAtInvocation = 2 };
        var sink = new DryRunPwmController(Identifiers, recorder.Callback);

        // First operation records and commits; the second record throws.
        sink.ConfigureOutput(12, 8000);
        Assert.Throws<InvalidOperationException>(() => sink.ConfigureOutput(13, 8000));

        // PWM2 never committed: a duty write to it is rejected as
        // not-configured (recorded, then thrown), while PWM1 still behaves
        // as configured. Records: configure-12 (committed), rejected
        // duty-13, accepted duty-12.
        Assert.Throws<InvalidOperationException>(() => sink.SetDutyCycle(13, 50));
        sink.SetDutyCycle(12, 50);
        Assert.Equal(3, recorder.Records.Count);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DryRunPwmController(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(
            () => new DryRunPwmController(Identifiers, null!));
    }
}
