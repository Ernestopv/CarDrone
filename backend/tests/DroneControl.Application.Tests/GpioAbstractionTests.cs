using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// PC-only GPIO abstraction tests. These prove parsing and mock behavior;
/// they intentionally do not claim the Task 24-dependent Raspberry adapter
/// can access a physical GPIO device.
/// </summary>
public class GpioAbstractionTests
{
    private sealed class FakeRaspberryGpioPlatform : IRaspberryGpioPlatform
    {
        public List<string> Calls { get; } = [];
        public Exception? ConfigureFailure { get; set; }
        public Exception? WriteFailure { get; set; }
        public int DisposeCount { get; private set; }

        public void ConfigureOutput(int lineIdentifier)
        {
            Calls.Add($"configure:{lineIdentifier}");
            if (ConfigureFailure is not null)
            {
                throw ConfigureFailure;
            }
        }

        public void Write(int lineIdentifier, GpioPinValue value)
        {
            Calls.Add($"write:{lineIdentifier}:{value}");
            if (WriteFailure is not null)
            {
                throw WriteFailure;
            }
        }

        public void Dispose() => DisposeCount++;
    }

    private static Dictionary<string, string?> ValidSection() => new()
    {
        ["Pin1"] = "23",
        ["Pin2"] = "24",
        ["Pin3"] = "21",
        ["Pin4"] = "20",
        ["PWM1"] = "12",
        ["PWM2"] = "13",
    };

    [Fact]
    public void Configuration_BindsFourNamedIdentifiers_AndLeavesPwmUnconsumed()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());

        Assert.Equal(23, config.Pin1);
        Assert.Equal(24, config.Pin2);
        Assert.Equal(21, config.Pin3);
        Assert.Equal(20, config.Pin4);
        Assert.Equal(new[] { 23, 24, 21, 20 }, config.DigitalLineIdentifiers);
        // No PWM properties or values are exposed/consumed by this abstraction.
        Assert.DoesNotContain(12, config.DigitalLineIdentifiers);
        Assert.DoesNotContain(13, config.DigitalLineIdentifiers);
    }

    [Theory]
    [InlineData("Pin1")]
    [InlineData("Pin2")]
    [InlineData("Pin3")]
    [InlineData("Pin4")]
    public void Configuration_MissingRequiredValue_Throws(string missing)
    {
        var section = ValidSection();
        section.Remove(missing);

        Assert.Throws<ArgumentException>(() => GpioPinConfiguration.FromSection(section));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" ")]
    public void Configuration_MalformedRequiredValue_Throws(string malformed)
    {
        var section = ValidSection();
        section["Pin2"] = malformed;

        Assert.Throws<ArgumentException>(() => GpioPinConfiguration.FromSection(section));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-23")]
    public void Configuration_NegativeIdentifier_Throws(string negative)
    {
        var section = ValidSection();
        section["Pin3"] = negative;

        Assert.Throws<ArgumentException>(() => GpioPinConfiguration.FromSection(section));
    }

    [Fact]
    public void Configuration_DuplicateIdentifiers_Throw()
    {
        var section = ValidSection();
        section["Pin4"] = section["Pin2"];

        Assert.Throws<ArgumentException>(() => GpioPinConfiguration.FromSection(section));
    }

    [Fact]
    public void Configuration_RejectsIdentifiersOutsideVerifiedCapabilitySet()
    {
        var supported = new HashSet<int> { 20, 21, 22, 23 };

        var ex = Assert.Throws<ArgumentException>(() =>
            GpioPinConfiguration.FromSection(ValidSection(), supported));

        Assert.Contains("unsupported", ex.Message);
        Assert.Contains("24", ex.Message);
    }

    [Fact]
    public void Mock_ConfiguresOnlyConfiguredOutputLines()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        using var gpio = new MockGpioController(config);

        gpio.ConfigureOutput(config.Pin1);
        gpio.ConfigureOutput(config.Pin4);

        Assert.Equal(new HashSet<int> { 23, 20 }, gpio.ConfiguredOutputs);
        Assert.Empty(gpio.LatestValues);
    }

    [Fact]
    public void Mock_WriteRequiresConfigureOutputFirst()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        using var gpio = new MockGpioController(config);

        Assert.Throws<InvalidOperationException>(() => gpio.Write(config.Pin1, GpioPinValue.High));
        Assert.Empty(gpio.LatestValues);
    }

    [Fact]
    public void Mock_WriteRecordsLatestDigitalValueDeterministically()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        using var gpio = new MockGpioController(config);
        gpio.ConfigureOutput(config.Pin1);

        gpio.Write(config.Pin1, GpioPinValue.High);
        gpio.Write(config.Pin1, GpioPinValue.Low);

        Assert.Equal(GpioPinValue.Low, gpio.LatestValues[config.Pin1]);
    }

    [Fact]
    public void Mock_RejectsUnconfiguredIdentifier()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        using var gpio = new MockGpioController(config);

        Assert.Throws<ArgumentException>(() => gpio.ConfigureOutput(99));
    }

    [Fact]
    public void Mock_RejectsUndefinedDigitalValueWithoutRecording()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        using var gpio = new MockGpioController(config);
        gpio.ConfigureOutput(config.Pin1);

        Assert.Throws<ArgumentOutOfRangeException>(() => gpio.Write(config.Pin1, (GpioPinValue)42));
        Assert.Empty(gpio.LatestValues);
    }

    [Fact]
    public void Mock_DisposalMakesFurtherOperationsFailDeterministically()
    {
        var gpio = new MockGpioController(GpioPinConfiguration.FromSection(ValidSection()));
        gpio.Dispose();

        Assert.Throws<ObjectDisposedException>(() => gpio.ConfigureOutput(23));
        Assert.Throws<ObjectDisposedException>(() => gpio.Write(23, GpioPinValue.Low));
    }

    [Fact]
    public void RaspberryAdapter_ForwardsConfiguredIdentifierAndLevelUnchanged()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        var platform = new FakeRaspberryGpioPlatform();
        using var gpio = new RaspberryGpioController(config, platform);

        gpio.ConfigureOutput(config.Pin3);
        gpio.Write(config.Pin3, GpioPinValue.High);

        Assert.Equal(new[] { "configure:21", "write:21:High" }, platform.Calls);
    }

    [Fact]
    public void RaspberryAdapter_RejectsUnconfiguredOrNotOutputLinesBeforePlatformCall()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        var platform = new FakeRaspberryGpioPlatform();
        using var gpio = new RaspberryGpioController(config, platform);

        Assert.Throws<ArgumentException>(() => gpio.ConfigureOutput(99));
        Assert.Throws<InvalidOperationException>(() => gpio.Write(config.Pin1, GpioPinValue.Low));
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void RaspberryAdapter_PropagatesConfigureAndWriteFailures()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        var configureFailure = new IOException("platform configure failed");
        var platform = new FakeRaspberryGpioPlatform { ConfigureFailure = configureFailure };
        using var gpio = new RaspberryGpioController(config, platform);

        Assert.Same(configureFailure, Assert.Throws<IOException>(() => gpio.ConfigureOutput(config.Pin1)));
        Assert.Throws<InvalidOperationException>(() => gpio.Write(config.Pin1, GpioPinValue.High));

        platform.ConfigureFailure = null;
        gpio.ConfigureOutput(config.Pin1);
        var writeFailure = new UnauthorizedAccessException("platform write denied");
        platform.WriteFailure = writeFailure;

        Assert.Same(writeFailure, Assert.Throws<UnauthorizedAccessException>(
            () => gpio.Write(config.Pin1, GpioPinValue.High)));
    }

    [Fact]
    public void RaspberryAdapter_PropagatesMissingDeviceFailure()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        var missingDevice = new FileNotFoundException("platform device unavailable");
        var platform = new FakeRaspberryGpioPlatform { ConfigureFailure = missingDevice };
        using var gpio = new RaspberryGpioController(config, platform);

        Assert.Same(missingDevice, Assert.Throws<FileNotFoundException>(
            () => gpio.ConfigureOutput(config.Pin1)));
    }

    [Fact]
    public void RaspberryAdapter_DisposesOwnedPlatformOnce_AndRejectsUseAfterDispose()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        var platform = new FakeRaspberryGpioPlatform();
        var gpio = new RaspberryGpioController(config, platform);

        gpio.Dispose();
        gpio.Dispose();

        Assert.Equal(1, platform.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => gpio.ConfigureOutput(config.Pin1));
        Assert.Throws<ObjectDisposedException>(() => gpio.Write(config.Pin1, GpioPinValue.Low));
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void RaspberryAdapter_PropagatesCancellationFromPlatformApi()
    {
        var config = GpioPinConfiguration.FromSection(ValidSection());
        var platform = new FakeRaspberryGpioPlatform
        {
            WriteFailure = new OperationCanceledException("platform operation cancelled"),
        };
        using var gpio = new RaspberryGpioController(config, platform);
        gpio.ConfigureOutput(config.Pin1);

        Assert.Throws<OperationCanceledException>(() => gpio.Write(config.Pin1, GpioPinValue.Low));
    }
}
