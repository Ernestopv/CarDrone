using DroneControl.Api;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DroneControl.Api.Tests;

/// <summary>
/// Composition tests for the Task 30 activation matrix
/// (specs/hardware/pwm.md). Frequency and duty values are a SYNTHETIC TEST
/// FIXTURE — arbitrary test data with no physical or electrical meaning,
/// never to be reused as hardware data. These tests prove software
/// composition and fail-fast behavior only; no physical actuation is claimed
/// or implied, and no test constructs a graph containing a physical PWM sink.
/// </summary>
public class PwmMappingCompositionTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static (string Key, string Value)[] GpioConfig() =>
    [
        ("GPIO:Pin1", "23"),
        ("GPIO:Pin2", "24"),
        ("GPIO:Pin3", "21"),
        ("GPIO:Pin4", "20"),
        ("GPIO:PWM1", "12"),
        ("GPIO:PWM2", "13"),
    ];

    /// <summary>Synthetic asserted-envelope fixture (test data only).</summary>
    private static (string Key, string Value)[] Envelope(bool verified = true) =>
    [
        ("PwmMapping:SpeedMappingVerified", verified ? "true" : "false"),
        ("PwmMapping:FrequencyHz", "8000"),
        ("PwmMapping:MinDutyPercent", "20"),
        ("PwmMapping:MaxDutyPercent", "80"),
    ];

    /// <summary>Synthetic verified direction fixture (test data only).</summary>
    private static (string Key, string Value)[] Direction(bool verified = true) =>
    [
        ("MotorMapping:DirectionMappingVerified", verified ? "true" : "false"),
        ("MotorMapping:Commands:forward:Pin1", "High"),
        ("MotorMapping:Commands:forward:Pin2", "Low"),
        ("MotorMapping:Commands:forward:Pin3", "High"),
        ("MotorMapping:Commands:forward:Pin4", "Low"),
        ("MotorMapping:Commands:backward:Pin1", "Low"),
        ("MotorMapping:Commands:backward:Pin2", "High"),
        ("MotorMapping:Commands:backward:Pin3", "Low"),
        ("MotorMapping:Commands:backward:Pin4", "High"),
        ("MotorMapping:Commands:left:Pin1", "High"),
        ("MotorMapping:Commands:left:Pin2", "High"),
        ("MotorMapping:Commands:left:Pin3", "Low"),
        ("MotorMapping:Commands:left:Pin4", "Low"),
        ("MotorMapping:Commands:right:Pin1", "Low"),
        ("MotorMapping:Commands:right:Pin2", "Low"),
        ("MotorMapping:Commands:right:Pin3", "High"),
        ("MotorMapping:Commands:right:Pin4", "High"),
        ("MotorMapping:Commands:stop:Pin1", "Low"),
        ("MotorMapping:Commands:stop:Pin2", "Low"),
        ("MotorMapping:Commands:stop:Pin3", "Low"),
        ("MotorMapping:Commands:stop:Pin4", "Low"),
    ];

    private static (string Key, string Value)[] DryRunConfig(params (string Key, string Value)[] extra)
        => [.. new[] { ("HARDWARE_MODE", "dry-run") }, .. GpioConfig(), .. extra];

    private static (string Key, string Value)[] RealConfig(params (string Key, string Value)[] extra)
        => [.. new[] { ("HARDWARE_MODE", "real") }, .. GpioConfig(), .. extra];

    /// <summary>
    /// Adds an observable PWM sink to the graph (same type the graph
    /// registers — only the record callback is observable; last registration
    /// wins for single resolution).
    /// </summary>
    private static List<DryRunOperationRecord> ObservePwm(IServiceCollection services)
    {
        var records = new List<DryRunOperationRecord>();
        services.AddSingleton<IPwmController>(_ => new DryRunPwmController([12, 13], records.Add));
        return records;
    }

    private static void AssertNoPwmRegistrations(IServiceCollection services)
    {
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(PwmMapping));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(SpeedController));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPwmController));
    }

    private static void AssertNoHardwareProvider(IServiceCollection services)
        => Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDroneHardware));

    /// <summary>Test-only fake for guard tests; performs no hardware work.</summary>
    private sealed class FakeRaspberryPwmPlatform : IRaspberryPwmPlatform
    {
        public void ConfigureOutput(int identifier, int frequencyHz)
        {
        }

        public void SetDutyCycle(int identifier, int dutyPercent)
        {
        }

        public void Dispose()
        {
        }
    }

    // --- Activation matrix: inactive states register nothing ---

    [Fact]
    public async Task PwmSectionAbsent_DryRunGraph_NoPwmRegistrations_InertSpeed_ZeroSinkOperations()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(Config(DryRunConfig()), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.DryRun, mode);
        AssertNoPwmRegistrations(services);

        var records = ObservePwm(services);
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Unavailable, await hardware.GetAvailabilityAsync());

        // Fully inert: the established default message, verbatim.
        var thrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));
        Assert.Equal("The drone implementation is unavailable.", thrown.Message);
        Assert.Empty(records);
    }

    [Fact]
    public async Task ValidUnassertedPwm_DryRunGraph_ValidatedButUnused_Inert()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(DryRunConfig(Envelope(verified: false))), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.DryRun, mode);
        AssertNoPwmRegistrations(services);

        var records = ObservePwm(services);
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        var thrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));
        Assert.Equal("The drone implementation is unavailable.", thrown.Message);
        Assert.Empty(records);
    }

    [Fact]
    public async Task AssertedPwm_DryRunGraph_IsActive_Available_AndAppliesTheFourRecordSequence()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(DryRunConfig(Envelope())), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.DryRun, mode);
        Assert.Contains(services, d => d.ServiceType == typeof(PwmMapping));
        Assert.Contains(services, d => d.ServiceType == typeof(IPwmController));
        Assert.Contains(services, d => d.ServiceType == typeof(SpeedController));
        Assert.Contains(services, d => d.ServiceType == typeof(IDroneHardware));

        var records = ObservePwm(services);
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Available, await hardware.GetAvailabilityAsync());

        await hardware.ApplySpeedAsync(45);

        // Exactly four suppressed records: configure x2 then duty x2,
        // PWM1 before PWM2 (synthetic fixture duty for speed 45: 47).
        Assert.Equal(
            new[]
            {
                "configure frequency-hz=8000", "configure frequency-hz=8000",
                "write duty-percent=47", "write duty-percent=47",
            },
            records.Select(record => record.RequestedParameters).ToArray());
        Assert.Equal(new[] { "12", "13", "12", "13" },
            records.Select(record => record.Identifier).ToArray());
        Assert.Equal(new[] { 1L, 2L, 3L, 4L },
            records.Select(record => record.Sequence).ToArray());
        Assert.All(records, record =>
        {
            Assert.Equal(HardwareOutputCategory.Pwm, record.Category);
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
        });

        // Out-of-range stays rejected at the provider boundary with zero
        // additional sink operations (reject, never clamp).
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => hardware.ApplySpeedAsync(101));
        Assert.Equal(4, records.Count);
    }

    [Theory]
    [InlineData("missing-frequency")]
    [InlineData("zero-frequency")]
    [InlineData("noninteger-frequency")]
    [InlineData("min-out-of-range")]
    [InlineData("max-below-min")]
    [InlineData("unknown-entry")]
    [InlineData("malformed-flag")]
    [InlineData("missing-pwm1")]
    [InlineData("duplicate-pwm")]
    [InlineData("pwm-collides-with-pin")]
    [InlineData("unverified-but-invalid")]
    public void InvalidEnvelopeShape_AbortsStartup_WithTheFullErrorList_NothingRegistered(string shape)
    {
        var config = ShapeConfig(shape);

        var services = new ServiceCollection();
        services.AddLogging();

        var thrown = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(config), raspberryRuntimeSupported: true));

        Assert.Contains("invalid PwmMapping section", thrown.Message);
        Assert.Contains(Fragment(shape), thrown.Message);

        // Fail-fast happened BEFORE any registration (D5) — and before any
        // GPIO/provider registration, in every shape.
        AssertNoPwmRegistrations(services);
        AssertNoHardwareProvider(services);
    }

    private static string Fragment(string shape)
        => shape switch
        {
            "missing-frequency" => "FrequencyHz must be a positive integer (found '<empty>')",
            "zero-frequency" => "FrequencyHz must be a positive integer (found '0')",
            "noninteger-frequency" => "FrequencyHz must be a positive integer (found 'abc')",
            "min-out-of-range" => "MinDutyPercent must be an integer between 0 and 100 (found '150')",
            "max-below-min" => "MaxDutyPercent must be greater than or equal to MinDutyPercent",
            "unknown-entry" => "Unknown PwmMapping entry 'UpdateInterval'",
            "malformed-flag" => "SpeedMappingVerified must be 'true' or 'false'",
            "missing-pwm1" => "GPIO:PWM1 must be a positive integer (found '<empty>')",
            "duplicate-pwm" => "GPIO:PWM1 and GPIO:PWM2 must be distinct identifiers (both '12')",
            "pwm-collides-with-pin" => "GPIO:PWM1 must not collide with the digital identifiers Pin1-Pin4",
            "unverified-but-invalid" => "FrequencyHz must be a positive integer (found '0')",
            _ => throw new InvalidOperationException($"Unknown shape '{shape}'."),
        };

    private static (string Key, string Value)[] ShapeConfig(string shape)
    {
        var values = new List<(string Key, string Value)>
        {
            ("HARDWARE_MODE", "dry-run"),
        };
        values.AddRange(GpioConfig());
        values.AddRange(Envelope());

        void Replace(string key, string value)
        {
            var index = values.FindIndex(pair => pair.Key == key);
            values[index] = (key, value);
        }

        switch (shape)
        {
            case "missing-frequency":
                values.RemoveAll(pair => pair.Key == "PwmMapping:FrequencyHz");
                break;
            case "zero-frequency":
                Replace("PwmMapping:FrequencyHz", "0");
                break;
            case "noninteger-frequency":
                Replace("PwmMapping:FrequencyHz", "abc");
                break;
            case "min-out-of-range":
                Replace("PwmMapping:MinDutyPercent", "150");
                break;
            case "max-below-min":
                Replace("PwmMapping:MinDutyPercent", "80");
                Replace("PwmMapping:MaxDutyPercent", "20");
                break;
            case "unknown-entry":
                values.Add(("PwmMapping:UpdateInterval", "5"));
                break;
            case "malformed-flag":
                Replace("PwmMapping:SpeedMappingVerified", "yes");
                break;
            case "missing-pwm1":
                values.RemoveAll(pair => pair.Key == "GPIO:PWM1");
                break;
            case "duplicate-pwm":
                Replace("GPIO:PWM2", "12");
                break;
            case "pwm-collides-with-pin":
                Replace("GPIO:PWM1", "23");
                break;
            case "unverified-but-invalid":
                // Validation runs regardless of the flag (matrix row
                // "present, invalid | any").
                Replace("PwmMapping:SpeedMappingVerified", "false");
                Replace("PwmMapping:FrequencyHz", "0");
                break;
            default:
                throw new InvalidOperationException($"Unknown shape '{shape}'.");
        }

        return [.. values];
    }

    // --- Mode gates ---

    [Fact]
    public void AssertedPwm_RealMode_WithoutRaspberryConfig_AbortsNamingMissingConfig()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var thrown = Assert.Throws<HostAbortedException>(() => services.AddDroneRuntime(
            Config(RealConfig(Envelope())), raspberryRuntimeSupported: true));

        Assert.Contains("Raspberry:PWM", thrown.Message);
        Assert.Contains("Refusing to start", thrown.Message);
        AssertNoHardwareProvider(services);
        AssertNoPwmRegistrations(services);
    }

    [Fact]
    public void AssertedPwm_RealMode_MissingChannelForAnIdentifier_AbortsNamingIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Chip path supplied, but no channel mapping for a PWM identifier:
        // fail-fast before any graph registration (D5).
        var thrown = Assert.Throws<HostAbortedException>(() => services.AddDroneRuntime(
            Config(
            [
                .. RealConfig(Envelope()),
                ("Raspberry:PWM:ChipPath", "/sys/class/pwm/pwmchip0"),
            ]),
            raspberryRuntimeSupported: true));

        Assert.Contains("Raspberry:PWM:Channels:", thrown.Message);
        Assert.Contains("Refusing to start", thrown.Message);
        AssertNoHardwareProvider(services);
        AssertNoPwmRegistrations(services);
    }

    [Fact]
    public async Task AssertedPwm_RealMode_WithRaspberryConfig_RegistersTheRealPwmSinkAndActivates()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(
            [
                .. RealConfig(Envelope()),
                ("Raspberry:PWM:ChipPath", "/sys/class/pwm/pwmchip0"),
                ("Raspberry:PWM:Channels:12", "0"),
                ("Raspberry:PWM:Channels:13", "1"),
            ]),
            raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.Real, mode);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<RaspberryPwmPlatform>(provider.GetRequiredService<IRaspberryPwmPlatform>());
        Assert.IsType<RaspberryPwmController>(provider.GetRequiredService<IPwmController>());
        Assert.NotNull(provider.GetService<SpeedController>());

        var hardware = provider.GetRequiredService<IDroneHardware>();
        var availability = await hardware.GetAvailabilityAsync();
        Assert.Equal(HardwareAvailability.Available, availability);
    }

    [Fact]
    public async Task UnassertedPwm_RealMode_StartsInert_WithNoPwmRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(RealConfig(Envelope(verified: false))), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.Real, mode);
        AssertNoPwmRegistrations(services);

        using var provider = services.BuildServiceProvider();
        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Unavailable, await hardware.GetAvailabilityAsync());
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));
    }

    [Fact]
    public void MockMode_NeverReadsThePwmSection_RegistrationsAreIdenticalWithAndWithoutIt()
    {
        var servicesWithout = new ServiceCollection();
        servicesWithout.AddLogging();
        servicesWithout.AddDroneRuntime(
            Config(("HARDWARE_MODE", "mock")), raspberryRuntimeSupported: true);

        var servicesWith = new ServiceCollection();
        servicesWith.AddLogging();
        // Even an ASSERTED section is ignored: the mock graph is untouched.
        servicesWith.AddDroneRuntime(
            Config([.. new[] { ("HARDWARE_MODE", "mock") }, .. Envelope()]),
            raspberryRuntimeSupported: true);

        static string Describe(ServiceDescriptor descriptor)
            => $"{descriptor.ServiceType.FullName}|{descriptor.ImplementationType?.FullName ?? "factory"}";

        Assert.Equal(
            servicesWithout.Select(Describe).OrderBy(value => value, StringComparer.Ordinal),
            servicesWith.Select(Describe).OrderBy(value => value, StringComparer.Ordinal));

        AssertNoPwmRegistrations(servicesWith);
        AssertNoHardwareProvider(servicesWith);
        Assert.Contains(servicesWith, d => d.ImplementationType == typeof(MockDroneController));
    }

    // --- Combined matrix with MotorMapping ---

    [Fact]
    public async Task BothMappingsAsserted_DryRun_BothCapabilitiesWork()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(
            Config(DryRunConfig([.. Direction(), .. Envelope()])),
            raspberryRuntimeSupported: true);

        var records = ObservePwm(services);
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Available, await hardware.GetAvailabilityAsync());

        var result = await hardware.ExecuteCommandAsync(DroneCommand.Forward);
        Assert.Equal(HardwareCommandStatus.Applied, result.Status);

        await hardware.ApplySpeedAsync(45);
        Assert.Equal(4, records.Count);
        Assert.Equal("write duty-percent=47", records[^1].RequestedParameters);
    }

    [Fact]
    public async Task DirectionOnly_CommandsWork_SpeedRefusedWithSpecificPwmMappingMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(
            Config(DryRunConfig(Direction())), raspberryRuntimeSupported: true);

        var records = ObservePwm(services);
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Available, await hardware.GetAvailabilityAsync());

        var result = await hardware.ExecuteCommandAsync(DroneCommand.Forward);
        Assert.Equal(HardwareCommandStatus.Applied, result.Status);

        var thrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));
        Assert.Contains("PwmMapping section", thrown.Message);
        Assert.Contains("SpeedMappingVerified", thrown.Message);
        Assert.Empty(records);
    }

    [Fact]
    public async Task PwmOnly_SpeedWorks_DirectionRefusedWithSpecificMotorMappingMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(
            Config(DryRunConfig(Envelope())), raspberryRuntimeSupported: true);

        var records = ObservePwm(services);
        var gpio = GpioPinConfiguration.FromSection(
            GpioConfig().ToDictionary(pair => pair.Key["GPIO:".Length..], pair => (string?)pair.Value));
        var directionRecords = new List<string>();
        services.AddSingleton<IGpioController>(
            _ => new DryRunGpioController(gpio, record => directionRecords.Add(record.RequestedParameters)));
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Available, await hardware.GetAvailabilityAsync());

        var thrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Forward));
        Assert.Contains("MotorMapping section", thrown.Message);
        Assert.Contains("DirectionMappingVerified", thrown.Message);
        Assert.Empty(directionRecords);

        await hardware.ApplySpeedAsync(45);
        Assert.Equal(4, records.Count);
    }

    [Fact]
    public async Task NeitherMappingAsserted_BothCapabilitiesUseTheDefaultMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(Config(DryRunConfig()), raspberryRuntimeSupported: true);

        using var provider = services.BuildServiceProvider();
        var hardware = provider.GetRequiredService<IDroneHardware>();

        var commandThrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Forward));
        var speedThrown = await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));

        Assert.Equal("The drone implementation is unavailable.", commandThrown.Message);
        Assert.Equal("The drone implementation is unavailable.", speedThrown.Message);
    }

    // --- GPIO-section-required rule and unsafe composition guard ---

    [Theory]
    [InlineData("dry-run")]
    [InlineData("real")]
    public void PwmSectionWithoutGpioConfiguration_AbortsInBothHardwareModes(string mode)
    {
        (string Key, string Value)[] withoutGpio = [("HARDWARE_MODE", mode), .. Envelope()];

        var services = new ServiceCollection();
        services.AddLogging();

        var thrown = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(withoutGpio), raspberryRuntimeSupported: true));

        Assert.Contains("requires valid GPIO configuration", thrown.Message);
        AssertNoPwmRegistrations(services);
        AssertNoHardwareProvider(services);
    }

    [Fact]
    public void UnsafeComposition_RealPwmPlatformInDryRun_Aborts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRaspberryPwmPlatform>(new FakeRaspberryPwmPlatform());

        var thrown = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(DryRunConfig()), raspberryRuntimeSupported: true));

        Assert.Contains("Unsafe composition", thrown.Message);
        Assert.Contains("IRaspberryPwmPlatform", thrown.Message);
        // No PWM capability is ever composed once the guard fires.
        AssertNoPwmRegistrations(services);
    }

    [Fact]
    public void UnsafeComposition_RealPwmControllerInDryRun_Aborts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(
            new RaspberryPwmController([12, 13], new FakeRaspberryPwmPlatform()));

        var thrown = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(DryRunConfig()), raspberryRuntimeSupported: true));

        Assert.Contains("Unsafe composition", thrown.Message);
        Assert.Contains("RaspberryPwmController", thrown.Message);
        AssertNoPwmRegistrations(services);
    }

    // --- Full stack through the safety controller (Task 26 unchanged) ---

    [Fact]
    public async Task FullStack_ConnectThenSpeed_AppliesThroughSafety_StatusRecordsRequestedSpeed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(
            Config(DryRunConfig([.. Direction(), .. Envelope()])),
            raspberryRuntimeSupported: true);
        var records = ObservePwm(services);
        using var provider = services.BuildServiceProvider();

        var safety = CreateSafety(provider.GetRequiredService<IDroneHardware>());
        await safety.StartSafetyAsync();
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
        await safety.ConnectAsync();

        await safety.SetSpeedAsync(45);

        Assert.Equal(4, records.Count);
        Assert.Equal("write duty-percent=47", records[^1].RequestedParameters);
        Assert.True(records[^1].DryRun);
        Assert.False(records[^1].PhysicallyApplied);

        var status = await safety.GetStatusAsync();
        Assert.Equal(45, status.State.Speed);
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
    }

    [Fact]
    public async Task FullStack_RecordCallbackFailure_KeepsTheTask26Transitions_WithoutRecordingSpeed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(
            Config(DryRunConfig([.. Direction(), .. Envelope()])),
            raspberryRuntimeSupported: true);
        // The record callback fails closed on the very first speed operation.
        services.AddSingleton<IPwmController>(
            _ => new DryRunPwmController([12, 13], _ => throw new InvalidOperationException("synthetic record failure")));
        using var provider = services.BuildServiceProvider();

        var safety = CreateSafety(provider.GetRequiredService<IDroneHardware>());
        await safety.StartSafetyAsync();
        await safety.ConnectAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => safety.SetSpeedAsync(45));
        Assert.Equal("synthetic record failure", thrown.Message);

        // Existing Task 26 reaction: STOP attempt succeeded → Safe (never a
        // fabricated success, never a silent swallow).
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);

        var status = await safety.GetStatusAsync();
        Assert.Equal(0, status.State.Speed);
    }

    private static DroneSafetyController CreateSafety(IDroneHardware hardware)
        => new(
            new HardwareDroneController(hardware),
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 200,
                stopTimeoutMilliseconds: 100,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

    // --- Shipped configuration ---

    [Fact]
    public void ShippedConfiguration_ContainsNoPwmMapping_AndKeepsTheD7DefaultFalse()
    {
        var shipped = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Assert.DoesNotContain("PwmMapping", shipped);
        Assert.DoesNotContain("FrequencyHz", shipped);
        Assert.DoesNotContain("DutyPercent", shipped);
        Assert.Contains("\"ExternalAbruptFailureProtectionVerified\": false", shipped);
    }
}
