using System.Runtime.InteropServices;
using DroneControl.Api;
using DroneControl.Application;
using DroneControl.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DroneControl.Api.Tests;

/// <summary>
/// Composition-root mode selection tests
/// (specs/hardware/raspberry-hardware-provider.md): default/mock keep today's
// graph; rejected or incompatible modes abort startup loudly.
/// </summary>
public class DroneRuntimeSelectionTests
{
    private static bool RunningOnPiRuntime =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        && RuntimeInformation.OSArchitecture == Architecture.Arm64;

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static (IServiceCollection Services, HardwareMode Mode) Register(IConfiguration config)
    {
        var services = new ServiceCollection();
        var mode = services.AddDroneRuntime(config);
        return (services, mode);
    }

    private static Type MockControllerRegistration(IServiceCollection services)
    {
        return services.Single(s => s.ServiceType == typeof(MockDroneController)).ImplementationType!;
    }

    [Fact]
    public void UnsetMode_RegistersSimulator_AndNoHardwareService()
    {
        var (services, mode) = Register(Config());

        Assert.Equal(HardwareMode.Mock, mode);
        Assert.Equal(typeof(MockDroneController), MockControllerRegistration(services));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IDroneHardware));
    }

    [Fact]
    public void ExplicitMock_RegistersSimulator()
    {
        var (services, mode) = Register(Config(("HARDWARE_MODE", "mock")));

        Assert.Equal(HardwareMode.Mock, mode);
        Assert.Equal(typeof(MockDroneController), MockControllerRegistration(services));
    }

    [Theory]
    [InlineData("MOCK")]
    [InlineData("  mock  ")]
    [InlineData("Mock")]
    public void MockValue_IsCaseInsensitiveAndTrimmed(string value)
    {
        var (_, mode) = Register(Config(("HARDWARE_MODE", value)));

        Assert.Equal(HardwareMode.Mock, mode);
    }

    [Fact]
    public void Real_OnUnsupportedPlatform_AbortsNamingModeAndRequirement()
    {
        var services = new ServiceCollection();
        var config = Config(("HARDWARE_MODE", "real"));

        if (RunningOnPiRuntime)
        {
            // On a real Pi runtime this legitimately passes the OS/arch gate.
            var mode = services.AddDroneRuntime(config);
            Assert.Equal(HardwareMode.Real, mode);
            Assert.Contains(services, s => s.ServiceType == typeof(IDroneHardware));
            return;
        }

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(config));
        Assert.Contains("'real'", ex.Message);
        Assert.Contains("linux/arm64", ex.Message);
    }

    [Fact]
    public void DryRun_OnUnsupportedPlatform_AbortsNamingModeAndRequirement()
    {
        var services = new ServiceCollection();

        if (RunningOnPiRuntime)
        {
            // On a Pi runtime the preflight passes; registration then needs
            // valid GPIO configuration (specs/hardware/dry-run.md).
            var mode = services.AddDroneRuntime(DryRunConfig());
            Assert.Equal(HardwareMode.DryRun, mode);
            Assert.Contains(services, s => s.ServiceType == typeof(IGpioController));
            return;
        }

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(("HARDWARE_MODE", "dry-run"))));
        Assert.Contains("'dry-run'", ex.Message);
        Assert.Contains("linux/arm64", ex.Message);
    }

    [Theory]
    [InlineData("DRY-RUN")]
    [InlineData("  dry-run  ")]
    [InlineData("Dry-Run")]
    public void DryRunValue_IsParsedCaseInsensitivelyAndTrimmed(string value)
    {
        if (RunningOnPiRuntime)
        {
            // On a Pi runtime the preflight passes and the mode resolves.
            var mode = new ServiceCollection().AddDroneRuntime(DryRunConfig(value));
            Assert.Equal(HardwareMode.DryRun, mode);
            return;
        }

        // Parsing must reach the platform preflight (not the invalid-value
        // branch): on a PC that means the mode-specific linux/arm64 abort.
        var ex = Assert.Throws<HostAbortedException>(
            () => new ServiceCollection().AddDroneRuntime(Config(("HARDWARE_MODE", value))));

        Assert.Contains("'dry-run'", ex.Message);
        Assert.DoesNotContain("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DryRunComposition_ResolvesHardwareFlowAndDryRunSink_WithoutAnyRealSink()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(DryRunConfig(), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.DryRun, mode);
        using var provider = services.BuildServiceProvider();

        // Same hardware-backed command flow as real mode.
        Assert.NotNull(provider.GetService<HardwareDroneController>());
        Assert.NotNull(provider.GetService<IDroneHardware>());

        // The only output sink in the graph is the suppressing dry-run sink;
        // the real sink types are absent, so they are structurally unreachable.
        var sink = provider.GetRequiredService<IGpioController>();
        Assert.IsType<DryRunGpioController>(sink);
        Assert.Null(provider.GetService<IRaspberryGpioPlatform>());
        Assert.Null(provider.GetService<RaspberryGpioController>());
    }

    [Fact]
    public void RealComposition_ResolvesSameHardwareFlow_WithNoOutputSinkYet()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mode = services.AddDroneRuntime(Config(("HARDWARE_MODE", "real")), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.Real, mode);
        using var provider = services.BuildServiceProvider();

        // Dry-run and real share the upstream flow; only the sink differs.
        // Real without an asserted mapping registers no output sink yet —
        // with an asserted MotorMapping/PwmMapping it registers the concrete
        // Linux sinks (see the Mapping/Pwm mapping composition tests).
        Assert.NotNull(provider.GetService<HardwareDroneController>());
        Assert.NotNull(provider.GetService<IDroneHardware>());
        Assert.Null(provider.GetService<IGpioController>());
    }

    [Fact]
    public void MockComposition_RegistersNoHardwareGraphAndNoRealSinks()
    {
        var services = new ServiceCollection();

        var mode = services.AddDroneRuntime(Config(), raspberryRuntimeSupported: true);

        Assert.Equal(HardwareMode.Mock, mode);
        // The mock graph contains no hardware provider, no generic GPIO/PWM
        // seam, and structurally no concrete Linux platform (isolation rule).
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDroneHardware));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IGpioController));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPwmController));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRaspberryGpioPlatform));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRaspberryPwmPlatform));
    }

    [Fact]
    public void DryRun_WithRealOutputSinkAlreadyRegistered_AbortsAsUnsafeComposition()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRaspberryGpioPlatform, ThrowingRaspberryPlatform>();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(DryRunConfig(), raspberryRuntimeSupported: true));

        Assert.Contains("dry-run", ex.Message);
        Assert.Contains("Unsafe composition", ex.Message);
    }

    [Fact]
    public void DryRun_WithMissingGpioConfiguration_AbortsBeforeCommandAcceptance()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(
                Config(("HARDWARE_MODE", "dry-run")), raspberryRuntimeSupported: true));

        Assert.Contains("GPIO", ex.Message);
    }

    [Fact]
    public void UnknownValue_AbortsListingAcceptedModes()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(("HARDWARE_MODE", "turbo"))));

        Assert.Contains("mock, dry-run, real", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PresentButEmptyValue_IsInvalidNotDefault(string value)
    {
        var services = new ServiceCollection();

        Assert.Throws<HostAbortedException>(
            () => services.AddDroneRuntime(Config(("HARDWARE_MODE", value))));
    }

    private static IConfiguration DryRunConfig(string modeValue = "dry-run")
        => Config(
            ("HARDWARE_MODE", modeValue),
            ("GPIO:Pin1", "23"),
            ("GPIO:Pin2", "24"),
            ("GPIO:Pin3", "21"),
            ("GPIO:Pin4", "20"),
            ("GPIO:PWM1", "12"),
            ("GPIO:PWM2", "13"));

    /// <summary>
    /// Stand-in real sink used only to prove the unsafe-composition guard.
    /// </summary>
    private sealed class ThrowingRaspberryPlatform : IRaspberryGpioPlatform
    {
        public void ConfigureOutput(int lineIdentifier)
            => throw new InvalidOperationException("A real sink must never run in a dry-run graph.");

        public void Write(int lineIdentifier, GpioPinValue value)
            => throw new InvalidOperationException("A real sink must never run in a dry-run graph.");

        public void Dispose()
        {
        }
    }
}
