using System.Net;
using DroneControl.Api.Controllers;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DroneControl.Api.Tests;

/// <summary>
/// Composition-root battery mode selection and endpoint tests
/// (specs/hardware/battery-monitoring.md): strict BATTERY_MODE parsing, the
/// linux/arm64 gate, the required validated Battery configuration, the
/// registration matrix, and GET /api/battery wire behavior.
/// </summary>
public class BatteryRuntimeCompositionTests
{
    // --- BATTERY_MODE parsing ---

    [Fact]
    public void UnsetBatteryMode_RegistersTheMockMonitor()
    {
        var services = new ServiceCollection();

        var mode = services.AddBatteryRuntime(Config(), raspberryRuntimeSupported: false);

        Assert.Equal(BatteryMode.Mock, mode);
        Assert.Contains(services, s => s.ServiceType == typeof(IBatteryMonitor)
            && s.ImplementationType == typeof(MockBatteryMonitor));
    }

    [Fact]
    public void ExplicitMock_MatchesUnset_AndIgnoresTheBatterySectionEntirely()
    {
        var unset = new ServiceCollection();
        var explicitMock = new ServiceCollection();

        var unsetMode = unset.AddBatteryRuntime(Config(), raspberryRuntimeSupported: false);
        // An invalid Battery section next to mock must be ignored: the section
        // is never read in mock mode.
        var mode = explicitMock.AddBatteryRuntime(
            Config(("BATTERY_MODE", "mock"), ("Battery:I2cAddress", "0x00")),
            raspberryRuntimeSupported: false);

        Assert.Equal(BatteryMode.Mock, unsetMode);
        Assert.Equal(BatteryMode.Mock, mode);
        Assert.Equal(unset.Count, explicitMock.Count);
        Assert.Equal(unset.Select(Describe), explicitMock.Select(Describe));
    }

    [Theory]
    [InlineData("MOCK")]
    [InlineData("  mock  ")]
    [InlineData("Mock")]
    public void MockValue_IsCaseInsensitiveAndTrimmed(string value)
    {
        var services = new ServiceCollection();

        var mode = services.AddBatteryRuntime(
            Config(("BATTERY_MODE", value)),
            raspberryRuntimeSupported: false);

        Assert.Equal(BatteryMode.Mock, mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ina219_misspelled")]
    public void InvalidBatteryMode_AbortsNamingTheKeyAndAcceptedValues(string value)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddBatteryRuntime(Config(("BATTERY_MODE", value)), raspberryRuntimeSupported: false));

        Assert.Contains("BATTERY_MODE", ex.Message);
        Assert.Contains("mock, ina219 (case-insensitive)", ex.Message);
        Assert.Empty(services);
    }

    // --- Platform gate ---

    [Fact]
    public void Ina219_OnUnsupportedPlatform_AbortsNamingModeAndRequirement()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddBatteryRuntime(
                Config(("BATTERY_MODE", "ina219")),
                raspberryRuntimeSupported: false));

        Assert.Contains("'ina219'", ex.Message);
        Assert.Contains("linux/arm64", ex.Message);
        Assert.Contains("Use 'mock' on a development PC.", ex.Message);
        Assert.Empty(services);
    }

    // --- Required Battery configuration ---

    [Fact]
    public void Ina219_WithoutBatteryConfig_AbortsBeforeAnyRegistration()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddBatteryRuntime(
                Config(("BATTERY_MODE", "ina219")),
                raspberryRuntimeSupported: true));

        Assert.Contains("invalid Battery configuration", ex.Message);
        Assert.Contains("I2cAddress", ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void Ina219_WithInvalidBatteryConfig_AbortsNamingTheViolation()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddBatteryRuntime(
                Config(
                    ("BATTERY_MODE", "ina219"),
                    ("Battery:I2cAddress", "0x40"),
                    ("Battery:FullVoltage", "6.0"),
                    ("Battery:EmptyVoltage", "8.4")),
                raspberryRuntimeSupported: true));

        Assert.Contains("EmptyVoltage must be lower", ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void Ina219_WithoutShuntOhms_AbortsNamingTheKey()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddBatteryRuntime(
                Config(
                    ("BATTERY_MODE", "ina219"),
                    ("Battery:I2cAddress", "0x42"),
                    ("Battery:FullVoltage", "8.4"),
                    ("Battery:EmptyVoltage", "6.0")),
                raspberryRuntimeSupported: true));

        Assert.Contains("ShuntOhms", ex.Message);
        Assert.Empty(services);
    }

    // --- Registration matrix ---

    [Fact]
    public void Ina219_ValidConfig_RegistersBusOptionsAndRealMonitor()
    {
        var services = new ServiceCollection();

        var mode = services.AddBatteryRuntime(
            Config(
                ("BATTERY_MODE", "ina219"),
                ("Battery:I2cBusPath", "/dev/i2c-1"),
                ("Battery:I2cAddress", "64"),
                ("Battery:FullVoltage", "8.4"),
                ("Battery:EmptyVoltage", "6.0"),
                ("Battery:ShuntOhms", "0.1")),
            raspberryRuntimeSupported: true);

        Assert.Equal(BatteryMode.Ina219, mode);
        Assert.Contains(services, s => s.ServiceType == typeof(BatteryOptions));
        Assert.Contains(services, s => s.ServiceType == typeof(II2cBus));
        Assert.Contains(services, s => s.ServiceType == typeof(IBatteryMonitor));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IBatteryMonitor)
            && s.ImplementationType == typeof(MockBatteryMonitor));

        // The factory-registered graph resolves to the real implementations
        // (no I2C device is opened by mere construction).
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        Assert.IsType<RaspberryI2cBus>(provider.GetRequiredService<II2cBus>());
        Assert.IsType<Ina219BatteryMonitor>(provider.GetRequiredService<IBatteryMonitor>());
    }

    [Fact]
    public void MockGraph_IsFreeOfRealSeams()
    {
        var services = new ServiceCollection();

        services.AddBatteryRuntime(Config(), raspberryRuntimeSupported: false);

        Assert.Contains(services, s => s.ServiceType == typeof(IBatteryMonitor)
            && s.ImplementationType == typeof(MockBatteryMonitor));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(II2cBus));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(BatteryOptions));
    }

    // --- Endpoint (real DI graph) ---

    [Fact]
    public async Task Battery_DefaultMockMode_ReturnsSimulatedReading()
    {
        using var factory = new BatteryApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/battery");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"available\":true", body);
        Assert.Contains("\"voltage\":7.8", body);
        Assert.Contains("\"percent\":64", body);
        Assert.Contains("\"state\":\"ok\"", body);
        Assert.Contains("\"simulated\":true", body);
        Assert.Contains("\"current\":-0.45", body);
        Assert.Contains("\"power\":-3.51", body);
    }

    [Fact]
    public async Task Battery_WithAnUnavailableMonitor_Returns200ErrorStatus()
    {
        using var factory = new BatteryApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IBatteryMonitor>(new FixedMonitor(new BatteryStatus
                {
                    Available = false,
                    Voltage = null,
                    Percent = null,
                    State = BatteryState.Error,
                }))));

        var response = await factory.CreateClient().GetAsync("/api/battery");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"available\":false", body);
        Assert.Contains("\"state\":\"error\"", body);
    }

    // --- Shipped configuration ---

    [Fact]
    public void ShippedConfiguration_ContainsNoBatterySection()
    {
        var shipped = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Assert.DoesNotContain("BATTERY_MODE", shipped);
        Assert.DoesNotContain("\"Battery\"", shipped);
    }

    // --- Helpers ---

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static string Describe(ServiceDescriptor descriptor)
        => $"{descriptor.ServiceType.FullName}/{descriptor.ImplementationType?.FullName ?? "factory"}/{descriptor.Lifetime}";

    private sealed class FixedMonitor(BatteryStatus status) : IBatteryMonitor
    {
        public Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(status);
    }

    private sealed class BatteryApiFactory : WebApplicationFactory<Program>
    {
    }
}