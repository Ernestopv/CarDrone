using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// PC-only tests for the Task 30 PWM envelope binding
/// (specs/hardware/pwm.md). The frequency and duty values below are a
/// SYNTHETIC TEST FIXTURE: deliberately arbitrary test data that encodes NO
/// physical or electrical meaning — never reuse them as hardware data, and
/// never read them as recommended values. These tests prove software
/// validation mechanics only; they cannot prove that any frequency or duty is
/// safe for a motor or the L298N (physical confirmation is deferred and
/// NOT VERIFIED).
/// </summary>
public class PwmMappingTests
{
    private static Dictionary<string, string?> ValidSection() => new()
    {
        ["FrequencyHz"] = "8000",
        ["MinDutyPercent"] = "20",
        ["MaxDutyPercent"] = "80",
        ["SpeedMappingVerified"] = "true",
    };

    private static Dictionary<string, string?> GpioValues() => new()
    {
        ["Pin1"] = "23",
        ["Pin2"] = "24",
        ["Pin3"] = "21",
        ["Pin4"] = "20",
        ["PWM1"] = "12",
        ["PWM2"] = "13",
    };

    private static GpioPinConfiguration Gpio()
        => GpioPinConfiguration.FromSection(GpioValues());

    private static PwmMapping? Bind(
        Dictionary<string, string?>? section = null,
        Dictionary<string, string?>? gpioValues = null)
        => PwmMapping.FromSection(section ?? ValidSection(), Gpio(), gpioValues ?? GpioValues());

    // --- Binding: valid section exposes every field and both identifiers ---

    [Fact]
    public void ValidSection_BindsAllFields_AndBothIdentifiers_InPwm1Pwm2Order()
    {
        var mapping = Bind();

        Assert.NotNull(mapping);
        Assert.Equal(8000, mapping.FrequencyHz);
        Assert.Equal(20, mapping.MinDutyPercent);
        Assert.Equal(80, mapping.MaxDutyPercent);
        Assert.True(mapping.SpeedMappingVerified);
        Assert.Equal(new[] { 12, 13 }, mapping.Identifiers);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("true")]
    [InlineData(" True ")]
    public void FlagParsing_IsCaseInsensitiveAndTrimmed(string rawFlag)
    {
        var section = ValidSection();
        section["SpeedMappingVerified"] = rawFlag;

        Assert.NotNull(Bind(section));
    }

    [Theory]
    [InlineData("frequencyhz")]
    [InlineData("FREQUENCYHZ")]
    public void SectionKeys_AreCaseInsensitive(string key)
    {
        var section = ValidSection();
        section[key] = section["FrequencyHz"];
        section.Remove("FrequencyHz");

        var mapping = Bind(section);
        Assert.NotNull(mapping);
        Assert.Equal(8000, mapping.FrequencyHz);
    }

    [Fact]
    public void FlagFalse_ValidSection_ReturnsNull_AfterFullValidation()
    {
        var section = ValidSection();
        section["SpeedMappingVerified"] = "false";

        // "Validated but unused": null result, no exception.
        Assert.Null(Bind(section));
    }

    [Fact]
    public void FlagAbsent_DefaultsToFalse_AndReturnsNull()
    {
        var section = ValidSection();
        section.Remove("SpeedMappingVerified");

        Assert.Null(Bind(section));
    }

    // --- Validation: flag does not bypass content validation ---

    [Theory]
    [InlineData("FrequencyHz")]
    [InlineData("MinDutyPercent")]
    [InlineData("MaxDutyPercent")]
    public void MissingRequiredValue_Throws_EvenWhenFlagFalse(string missing)
    {
        var section = ValidSection();
        section.Remove(missing);
        section["SpeedMappingVerified"] = "false";

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains(missing, thrown.Message);
        Assert.Contains("found '<empty>'", thrown.Message);
    }

    [Fact]
    public void FlagFalse_InvalidContent_StillThrows()
    {
        var section = ValidSection();
        section["SpeedMappingVerified"] = "false";
        section["FrequencyHz"] = "0";

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains("FrequencyHz must be a positive integer", thrown.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("")]
    public void NonPositiveOrMalformedFrequency_IsRejected(string raw)
    {
        var section = ValidSection();
        section["FrequencyHz"] = raw;

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains("FrequencyHz must be a positive integer", thrown.Message);
    }

    [Theory]
    [InlineData("MinDutyPercent", "101")]
    [InlineData("MinDutyPercent", "-1")]
    [InlineData("MinDutyPercent", "abc")]
    [InlineData("MaxDutyPercent", "150")]
    [InlineData("MaxDutyPercent", "-1")]
    [InlineData("MaxDutyPercent", "abc")]
    public void DutyBounds_MustBeIntegersBetween0And100(string key, string raw)
    {
        var section = ValidSection();
        section[key] = raw;

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains($"{key} must be an integer between 0 and 100", thrown.Message);
    }

    [Fact]
    public void MaxBelowMin_IsRejected()
    {
        var section = ValidSection();
        section["MinDutyPercent"] = "80";
        section["MaxDutyPercent"] = "20";

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains(
            "MaxDutyPercent must be greater than or equal to MinDutyPercent",
            thrown.Message);
    }

    [Fact]
    public void UnknownSectionEntry_IsRejected()
    {
        var section = ValidSection();
        section["UpdateInterval"] = "5";

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains("Unknown PwmMapping entry 'UpdateInterval'", thrown.Message);
    }

    [Fact]
    public void MalformedFlag_IsRejected_NeverCoerced()
    {
        var section = ValidSection();
        section["SpeedMappingVerified"] = "yes";

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section));
        Assert.Contains(
            "SpeedMappingVerified must be 'true' or 'false'",
            thrown.Message);
    }

    // --- Validation: PWM identifiers from the GPIO section ---

    [Theory]
    [InlineData("PWM1")]
    [InlineData("PWM2")]
    public void MissingPwmIdentifier_IsRejected(string missing)
    {
        var gpio = GpioValues();
        gpio.Remove(missing);

        var thrown = Assert.Throws<ArgumentException>(() => Bind(gpioValues: gpio));
        Assert.Contains($"GPIO:{missing} must be a positive integer (found '<empty>')", thrown.Message);
    }

    [Theory]
    [InlineData("PWM1", "abc")]
    [InlineData("PWM1", "0")]
    [InlineData("PWM1", "-12")]
    [InlineData("PWM2", "abc")]
    [InlineData("PWM2", "0")]
    [InlineData("PWM2", "-13")]
    public void MalformedPwmIdentifier_IsRejected(string key, string raw)
    {
        var gpio = GpioValues();
        gpio[key] = raw;

        var thrown = Assert.Throws<ArgumentException>(() => Bind(gpioValues: gpio));
        Assert.Contains($"GPIO:{key} must be a positive integer (found '{raw}')", thrown.Message);
    }

    [Fact]
    public void DuplicatePwmIdentifiers_AreRejected()
    {
        var gpio = GpioValues();
        gpio["PWM2"] = "12";

        var thrown = Assert.Throws<ArgumentException>(() => Bind(gpioValues: gpio));
        Assert.Contains("GPIO:PWM1 and GPIO:PWM2 must be distinct identifiers (both '12')", thrown.Message);
    }

    [Theory]
    [InlineData("PWM1", "23")]
    [InlineData("PWM2", "20")]
    public void PwmIdentifierCollidingWithDigitalIdentifier_IsRejected(string key, string raw)
    {
        var gpio = GpioValues();
        gpio[key] = raw;

        var thrown = Assert.Throws<ArgumentException>(() => Bind(gpioValues: gpio));
        Assert.Contains($"GPIO:{key} must not collide with the digital identifiers Pin1-Pin4", thrown.Message);
    }

    // --- Aggregation: every violation lands in one message ---

    [Fact]
    public void AllViolations_AreAggregatedIntoOneMessage()
    {
        var section = ValidSection();
        section["FrequencyHz"] = "0";
        section["MinDutyPercent"] = "150";
        section["UpdateInterval"] = "5";
        var gpio = GpioValues();
        gpio.Remove("PWM2");

        var thrown = Assert.Throws<ArgumentException>(() => Bind(section, gpio));
        Assert.Contains("PwmMapping validation failed (4 problem(s))", thrown.Message);
        Assert.Contains("FrequencyHz must be a positive integer (found '0')", thrown.Message);
        Assert.Contains("MinDutyPercent must be an integer between 0 and 100 (found '150')", thrown.Message);
        Assert.Contains("Unknown PwmMapping entry 'UpdateInterval'", thrown.Message);
        Assert.Contains("GPIO:PWM2 must be a positive integer (found '<empty>')", thrown.Message);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => PwmMapping.FromSection(null!, Gpio(), GpioValues()));
        Assert.Throws<ArgumentNullException>(
            () => PwmMapping.FromSection(ValidSection(), null!, GpioValues()));
        Assert.Throws<ArgumentNullException>(
            () => PwmMapping.FromSection(ValidSection(), Gpio(), null!));
    }
}
