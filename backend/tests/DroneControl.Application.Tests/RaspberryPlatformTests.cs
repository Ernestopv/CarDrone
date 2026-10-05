using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Construction/failure-contract tests for the concrete Linux platforms
/// (directive: finish HARDWARE_MODE=real). No real device I/O occurs here:
/// GPIO tests cover constructor and external-contract behavior only (any
/// platform call would reach P/Invoke libc on the target), while the PWM
/// sysfs behavior — including the deterministic write sequence and the
/// shutdown duty-0/disable sequence — is exercised against a fake in-memory
/// sysfs directory tree, never a real /sys/class/pwm node. Physical behavior
/// stays NOT VERIFIED (target Pi; Task 36).
/// </summary>
public class RaspberryPlatformTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void GpioPlatform_RequiresAChipPath()
    {
        Assert.Throws<ArgumentException>(() => new RaspberryGpioPlatform(""));
        Assert.Throws<ArgumentException>(() => new RaspberryGpioPlatform("   "));
    }

    [Fact]
    public void GpioPlatform_WriteBeforeConfigure_FailsDeterministicallyWithoutIo()
    {
        using var platform = new RaspberryGpioPlatform("/dev/gpiochip0");

        // The adapter contract requires configure-before-write; this throws
        // before any platform (ioctl) call is reached.
        var exception = Assert.Throws<InvalidOperationException>(
            () => platform.Write(23, GpioPinValue.High));
        Assert.Contains("not configured", exception.Message);
    }

    [Fact]
    public void GpioPlatform_Dispose_IsIdempotent()
    {
        using var platform = new RaspberryGpioPlatform("/dev/gpiochip0");
        platform.Dispose();
        platform.Dispose(); // No second pass may throw or double-close.
    }

    [Fact]
    public void PwmPlatform_RequiresAChipPathAndChannelMap()
    {
        Assert.Throws<ArgumentException>(() => new RaspberryPwmPlatform("", new Dictionary<int, int>()));
        Assert.Throws<ArgumentNullException>(() => new RaspberryPwmPlatform("/sys/class/pwm/pwmchip0", null!));
    }

    [Fact]
    public void PwmPlatform_ConfigureOutput_FailsClosedWhenTheChipDoesNotExist()
    {
        using var platform = new RaspberryPwmPlatform(
            "/definitely/not/a/real/pwmchip",
            new Dictionary<int, int> { [12] = 0 });

        // The chip is absent: configuration must fail loudly (wrapped IO
        // failure), never silently succeed or touch a wrong channel.
        var exception = Assert.Throws<InvalidOperationException>(
            () => platform.ConfigureOutput(12, 1000));
        Assert.Contains("PWM sysfs write failed", exception.Message);
    }

    [Fact]
    public void PwmPlatform_ConfigureOutput_RefusesAnIdentifierWithoutAChannel()
    {
        using var platform = new RaspberryPwmPlatform(
            "/definitely/not/a/real/pwmchip",
            new Dictionary<int, int>());

        var exception = Assert.Throws<ArgumentException>(
            () => platform.ConfigureOutput(13, 1000));
        Assert.Contains("no configured Raspberry:PWM:Channels mapping", exception.Message);
    }

    [Fact]
    public void PwmPlatform_FakeSysfs_WriteSequenceAndSafeShutdown_AreDeterministic()
    {
        var chip = CreateFakePwmChip();

        using (var platform = new RaspberryPwmPlatform(chip, new Dictionary<int, int> { [12] = 0 }))
        {
            // ConfigureOutput(12, 1000) => period 1,000,000 ns, duty 0, disabled.
            platform.ConfigureOutput(12, 1000);
            Assert.Equal("1000000", File.ReadAllText(Path.Combine(chip, "pwm0", "period")));
            Assert.Equal("0", File.ReadAllText(Path.Combine(chip, "pwm0", "duty_cycle")));
            Assert.Equal("0", File.ReadAllText(Path.Combine(chip, "pwm0", "enable")));

            // SetDutyCycle(12, 50) => duty = 500,000 ns, enabled.
            platform.SetDutyCycle(12, 50);
            Assert.Equal("500000", File.ReadAllText(Path.Combine(chip, "pwm0", "duty_cycle")));
            Assert.Equal("1", File.ReadAllText(Path.Combine(chip, "pwm0", "enable")));

            // Deterministic safe shutdown: duty 0 then disabled on Dispose.
            platform.Dispose();
            Assert.Equal("0", File.ReadAllText(Path.Combine(chip, "pwm0", "duty_cycle")));
            Assert.Equal("0", File.ReadAllText(Path.Combine(chip, "pwm0", "enable")));

            // Idempotent disposal must not throw.
            platform.Dispose();
        }
    }

    private string CreateFakePwmChip()
    {
        var chip = Path.Combine(Path.GetTempPath(), $"pwm-test-{Guid.NewGuid():N}", "pwmchip9");
        Directory.CreateDirectory(chip);
        Directory.CreateDirectory(Path.Combine(chip, "pwm0"));
        File.WriteAllText(Path.Combine(chip, "export"), string.Empty);
        File.WriteAllText(Path.Combine(chip, "unexport"), string.Empty);
        _tempDirectories.Add(Directory.GetParent(chip)!.FullName);
        return chip;
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone — fine.
            }
        }

        _tempDirectories.Clear();
    }
}