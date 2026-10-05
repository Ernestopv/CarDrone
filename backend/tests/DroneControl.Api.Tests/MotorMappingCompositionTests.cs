using DroneControl.Api;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DroneControl.Api.Tests;

/// <summary>
/// Composition tests for the Task 29 activation matrix
/// (specs/hardware/motor-control.md). Level values are a SYNTHETIC TEST
/// FIXTURE — arbitrary test data with no physical or wiring meaning, never to
/// be reused as hardware data. These tests prove software composition and
/// fail-fast behavior only; no physical actuation is claimed or implied.
/// </summary>
public class MotorMappingCompositionTests
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

    /// <summary>Synthetic verified mapping fixture (test data only).</summary>
    private static (string Key, string Value)[] Mapping(bool verified = true) =>
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

    // --- Activation matrix: inactive states stay byte-identical to today ---

    [Fact]
    public async Task MappingSectionAbsent_DryRunGraph_StaysInert_WithZeroSinkOperations()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(Config(DryRunConfig()), raspberryRuntimeSupported: true);

        // An observable sink in the graph proves the inactive provider issues
        // zero operations (last registration wins for single resolution).
        var sink = new RecordingSink();
        services.AddSingleton<IGpioController>(sink);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(HardwareMode.DryRun, mode);
        Assert.Null(provider.GetService<MotorController>());
        Assert.Null(provider.GetService<MotorMapping>());

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Unavailable, await hardware.GetAvailabilityAsync());
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Forward));
        Assert.Empty(sink.Calls);
    }

    [Fact]
    public async Task ValidUnverifiedMapping_DryRunGraph_ValidatedButUnused_Inert()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(DryRunConfig(Mapping(verified: false))), raspberryRuntimeSupported: true);

        var sink = new RecordingSink();
        services.AddSingleton<IGpioController>(sink);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(HardwareMode.DryRun, mode);
        Assert.Null(provider.GetService<MotorController>());
        Assert.Null(provider.GetService<MotorMapping>());

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Unavailable, await hardware.GetAvailabilityAsync());
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Stop));
        Assert.Empty(sink.Calls);
    }

    [Fact]
    public async Task UnverifiedMapping_RealMode_StaysInert_NoOutputSinkRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(RealConfig(Mapping(verified: false))), raspberryRuntimeSupported: true);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(HardwareMode.Real, mode);
        Assert.Null(provider.GetService<MotorController>());
        Assert.Null(provider.GetService<IGpioController>());

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Unavailable, await hardware.GetAvailabilityAsync());
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ExecuteCommandAsync(DroneCommand.Forward));
    }

    [Fact]
    public void MockMode_IgnoresTheMappingSection_AndRegistersNoMappingTypes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config([("HARDWARE_MODE", "mock"), .. Mapping(verified: true)]),
            raspberryRuntimeSupported: true);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(HardwareMode.Mock, mode);
        Assert.NotNull(provider.GetService<MockDroneController>());
        Assert.Null(provider.GetService<IDroneHardware>());
        Assert.Null(provider.GetService<MotorController>());
        Assert.Null(provider.GetService<MotorMapping>());
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(MotorController)
            || descriptor.ServiceType == typeof(MotorMapping));
    }

    // --- Activation matrix: verified + valid → active (dry-run only) ---

    [Fact]
    public async Task VerifiedValidMapping_DryRunGraph_IsActive_AvailableAndApplies()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDroneRuntime(Config(DryRunConfig(Mapping())), raspberryRuntimeSupported: true);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<MotorMapping>());
        Assert.NotNull(provider.GetService<MotorController>());

        // The only output sink remains the suppressing dry-run boundary.
        Assert.IsType<DryRunGpioController>(provider.GetRequiredService<IGpioController>());
        Assert.Null(provider.GetService<IRaspberryGpioPlatform>());
        Assert.Null(provider.GetService<RaspberryGpioController>());

        var hardware = provider.GetRequiredService<IDroneHardware>();
        Assert.Equal(HardwareAvailability.Available, await hardware.GetAvailabilityAsync());

        var result = await hardware.ExecuteCommandAsync(DroneCommand.Forward);
        Assert.Equal(HardwareCommandStatus.Applied, result.Status);

        // Speed stays explicitly refused while only the motor capability is
        // active (the message names the missing PwmMapping).
        await Assert.ThrowsAsync<DroneUnavailableException>(
            () => hardware.ApplySpeedAsync(45));
    }

    [Fact]
    public async Task VerifiedValidMapping_DryRunGraph_EveryOperationSuppressed_ThroughTheDryRunSink()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroneRuntime(Config(DryRunConfig(Mapping())), raspberryRuntimeSupported: true);

        // Capture the suppression records from a dry-run sink instance (same
        // type the graph registers — only the record callback is observable).
        var records = new List<DryRunOperationRecord>();
        var gpio = GpioPinConfiguration.FromSection(
            GpioConfig().ToDictionary(pair => pair.Key["GPIO:".Length..], pair => (string?)pair.Value));
        services.AddSingleton<IGpioController>(
            _ => new DryRunGpioController(gpio, records.Add));
        using var provider = services.BuildServiceProvider();

        var hardware = provider.GetRequiredService<IDroneHardware>();
        var result = await hardware.ExecuteCommandAsync(DroneCommand.Forward);
        Assert.Equal(HardwareCommandStatus.Applied, result.Status);

        Assert.Equal(
            new[]
            {
                "configure:23", "configure:24", "configure:21", "configure:20",
                "write:23:High", "write:24:Low", "write:21:High", "write:20:Low",
            },
            records.Select(record => record.RequestedParameters == "configure-output"
                ? $"configure:{record.Identifier}"
                : $"write:{record.Identifier}:{record.RequestedParameters["write value=".Length..]}")
                .ToArray());
        Assert.Equal(new[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L },
            records.Select(record => record.Sequence).ToArray());
        Assert.All(records, record =>
        {
            Assert.True(record.DryRun);
            Assert.False(record.PhysicallyApplied);
            Assert.Contains("suppress", record.SuppressionReason, StringComparison.OrdinalIgnoreCase);
        });
    }

    // --- Activation matrix: invalid content → startup abort, explicit list ---

    [Theory]
    [InlineData("missing-stop")]
    [InlineData("missing-command")]
    [InlineData("unknown-command")]
    [InlineData("incomplete-coverage")]
    [InlineData("bad-level-token")]
    [InlineData("unknown-identifier")]
    [InlineData("pwm-identifier")]
    [InlineData("malformed-flag")]
    [InlineData("assertion-without-data")]
    [InlineData("unknown-entry")]
    [InlineData("unverified-but-invalid")]
    public void InvalidMappingShape_AbortsStartup_WithTheFullErrorList_NoProviderRegistered(string shape)
    {
        var mapping = Mapping().ToList();
        string fragment;
        switch (shape)
        {
            case "missing-stop":
                mapping.RemoveAll(pair => pair.Key.StartsWith("MotorMapping:Commands:stop:"));
                fragment = "'stop' is missing";
                break;
            case "missing-command":
                mapping.RemoveAll(pair => pair.Key.StartsWith("MotorMapping:Commands:left:"));
                fragment = "'left' is missing";
                break;
            case "unknown-command":
                mapping.Add(("MotorMapping:Commands:hover:Pin1", "High"));
                fragment = "Unknown motor mapping command 'hover'";
                break;
            case "incomplete-coverage":
                mapping.RemoveAll(pair => pair.Key == "MotorMapping:Commands:forward:Pin3");
                fragment = "'forward' is missing a level for identifier 'Pin3'";
                break;
            case "bad-level-token":
                Replace(mapping, "MotorMapping:Commands:forward:Pin2", "Medium");
                fragment = "must be HIGH or LOW";
                break;
            case "unknown-identifier":
                mapping.Add(("MotorMapping:Commands:forward:Pin5", "High"));
                fragment = "'Pin5' is not accepted";
                break;
            case "pwm-identifier":
                mapping.Add(("MotorMapping:Commands:forward:PWM1", "High"));
                fragment = "PwmMapping";
                break;
            case "malformed-flag":
                Replace(mapping, "MotorMapping:DirectionMappingVerified", "yes");
                fragment = "DirectionMappingVerified must be 'true' or 'false'";
                break;
            case "assertion-without-data":
                mapping.RemoveAll(pair => pair.Key.StartsWith("MotorMapping:Commands:"));
                fragment = "no Commands mapping data is present";
                break;
            case "unknown-entry":
                mapping.Add(("MotorMapping:Speed", "50"));
                fragment = "Unknown MotorMapping entry 'Speed'";
                break;
            case "unverified-but-invalid":
                // Validation runs regardless of the flag (matrix row
                // "present, invalid | any").
                Replace(mapping, "MotorMapping:DirectionMappingVerified", "false");
                mapping.Add(("MotorMapping:Commands:hover:Pin1", "High"));
                fragment = "Unknown motor mapping command 'hover'";
                break;
            default:
                throw new InvalidOperationException($"Unknown shape '{shape}'.");
        }

        var services = new ServiceCollection();
        services.AddLogging();

        var thrown = Assert.Throws<HostAbortedException>(() => services.AddDroneRuntime(
            Config(DryRunConfig([.. mapping])), raspberryRuntimeSupported: true));

        Assert.Contains("MotorMapping", thrown.Message);
        Assert.Contains(fragment, thrown.Message);

        // Fail-fast happened BEFORE any graph registration (D5).
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDroneHardware));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(MotorController));
    }

    [Fact]
    public void MappingWithoutGpioConfiguration_AbortsInBothHardwareModes()
    {
        (string Key, string Value)[] withoutGpio = [("HARDWARE_MODE", "dry-run"), .. Mapping()];
        var dryRunServices = new ServiceCollection();
        dryRunServices.AddLogging();
        var dryRunThrown = Assert.Throws<HostAbortedException>(
            () => dryRunServices.AddDroneRuntime(Config(withoutGpio), raspberryRuntimeSupported: true));
        Assert.Contains("GPIO", dryRunThrown.Message);

        var realServices = new ServiceCollection();
        realServices.AddLogging();
        var realThrown = Assert.Throws<HostAbortedException>(
            () => realServices.AddDroneRuntime(
                Config([.. new[] { ("HARDWARE_MODE", "real") }, .. Mapping()]),
                raspberryRuntimeSupported: true));
        Assert.Contains("GPIO", realThrown.Message);
    }

    // --- Activation matrix: real mode + assertion → concrete Linux sink ---

    [Fact]
    public async Task VerifiedValidMapping_RealMode_RegistersTheRealGpioSinkAndActivates()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(
            Config(RealConfig(Mapping())), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.Real, mode);
        using var provider = services.BuildServiceProvider();

        // The concrete Linux GPIO platform (inventory-detected /dev/gpiochip0)
        // is wired into the adapter, and the asserted mapping activates the
        // provider's direction capability.
        Assert.IsType<RaspberryGpioPlatform>(provider.GetRequiredService<IRaspberryGpioPlatform>());
        Assert.IsType<RaspberryGpioController>(provider.GetRequiredService<IGpioController>());
        Assert.NotNull(provider.GetService<MotorController>());
        Assert.NotNull(provider.GetService<IDroneHardware>());

        var hardware = provider.GetRequiredService<IDroneHardware>();
        var availability = await hardware.GetAvailabilityAsync();
        Assert.Equal(HardwareAvailability.Available, availability);
    }

    private static void Replace(List<(string Key, string Value)> pairs, string key, string value)
    {
        pairs.RemoveAll(pair => pair.Key == key);
        pairs.Add((key, value));
    }
}
