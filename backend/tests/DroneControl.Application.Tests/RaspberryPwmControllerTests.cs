using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Tests for the Task 30 real-side seam
/// (specs/hardware/pwm.md): the adapter is proven ONLY against an in-memory
/// fake platform. No Linux PWM subsystem, device path, kernel API, or
/// library exists in these tests or in shipped code — the concrete
/// implementation is deferred to the prerequisites ledger.
/// </summary>
public class RaspberryPwmControllerTests
{
    private sealed class FakeRaspberryPwmPlatform : IRaspberryPwmPlatform
    {
        public List<string> Calls { get; } = [];
        public Exception? ConfigureFailure { get; set; }
        public Exception? DutyFailure { get; set; }
        public int DisposeCount { get; private set; }

        public void ConfigureOutput(int identifier, int frequencyHz)
        {
            Calls.Add($"configure:{identifier}:{frequencyHz}");
            if (ConfigureFailure is not null)
            {
                throw ConfigureFailure;
            }
        }

        public void SetDutyCycle(int identifier, int dutyPercent)
        {
            Calls.Add($"duty:{identifier}:{dutyPercent}");
            if (DutyFailure is not null)
            {
                throw DutyFailure;
            }
        }

        public void Dispose() => DisposeCount++;
    }

    private static IReadOnlyCollection<int> Identifiers => [12, 13];

    private static (RaspberryPwmController Controller, FakeRaspberryPwmPlatform Platform) Create()
    {
        var platform = new FakeRaspberryPwmPlatform();
        return (new RaspberryPwmController(Identifiers, platform), platform);
    }

    [Fact]
    public void ForwardsIdentifierFrequencyAndDuty_Unchanged()
    {
        var (controller, platform) = Create();

        controller.ConfigureOutput(12, 8000);
        controller.ConfigureOutput(13, 8000);
        controller.SetDutyCycle(12, 37);
        controller.SetDutyCycle(13, 37);

        Assert.Equal(
            new[]
            {
                "configure:12:8000", "configure:13:8000",
                "duty:12:37", "duty:13:37",
            },
            platform.Calls);
    }

    [Theory]
    [InlineData("unknown-configure")]
    [InlineData("bad-frequency")]
    [InlineData("duty-above-100")]
    [InlineData("duty-below-0")]
    [InlineData("write-before-configure")]
    public void ValidationRejections_Throw_BeforeThePlatformIsTouched(string rejection)
    {
        var (controller, platform) = Create();

        switch (rejection)
        {
            case "unknown-configure":
                Assert.Throws<ArgumentException>(() => controller.ConfigureOutput(99, 8000));
                break;
            case "bad-frequency":
                Assert.Throws<ArgumentOutOfRangeException>(() => controller.ConfigureOutput(12, 0));
                break;
            case "duty-above-100":
                Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetDutyCycle(12, 101));
                break;
            case "duty-below-0":
                Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetDutyCycle(13, -1));
                break;
            case "write-before-configure":
                Assert.Throws<InvalidOperationException>(() => controller.SetDutyCycle(12, 50));
                break;
        }

        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void UnknownIdentifierDutyWrite_IsRejected_BeforeTheConfiguredOutputCheck()
    {
        var (controller, platform) = Create();
        controller.ConfigureOutput(12, 8000);
        var before = platform.Calls.Count;

        Assert.Throws<ArgumentException>(() => controller.SetDutyCycle(99, 50));

        Assert.Equal(before, platform.Calls.Count);
    }

    [Fact]
    public void PlatformFailures_PropagateToTheCaller()
    {
        var (controller, platform) = Create();
        platform.ConfigureFailure = new InvalidOperationException("synthetic platform configure failure");

        Assert.Throws<InvalidOperationException>(
            () => controller.ConfigureOutput(12, 8000));

        platform.ConfigureFailure = null;
        platform.DutyFailure = new InvalidOperationException("synthetic platform duty failure");
        controller.ConfigureOutput(12, 8000);

        Assert.Throws<InvalidOperationException>(() => controller.SetDutyCycle(12, 50));
    }

    [Fact]
    public void Dispose_DisposesThePlatformOnce_AndRejectsUseAfterDispose()
    {
        var (controller, platform) = Create();
        controller.ConfigureOutput(12, 8000);

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(1, platform.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => controller.ConfigureOutput(13, 8000));
        Assert.Throws<ObjectDisposedException>(() => controller.SetDutyCycle(12, 50));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var platform = new FakeRaspberryPwmPlatform();
        Assert.Throws<ArgumentNullException>(
            () => new RaspberryPwmController(null!, platform));
        Assert.Throws<ArgumentNullException>(
            () => new RaspberryPwmController(Identifiers, null!));
    }
}
