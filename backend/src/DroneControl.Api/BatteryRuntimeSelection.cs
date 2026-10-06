using System.Runtime.InteropServices;
using DroneControl.Application;
using DroneControl.Infrastructure;

namespace DroneControl.Api;

/// <summary>
/// The battery runtime mode selected from configuration (decisions D3/D5 of
/// specs/architecture/runtime-deployment.md, implemented by
/// specs/hardware/battery-monitoring.md). Selection lives in the composition
/// root; the mode is read once at startup and nothing can switch it while the
/// process runs.
/// </summary>
public enum BatteryMode
{
    /// <summary>Simulated reading — today's PC behavior (default).</summary>
    Mock,

    /// <summary>Real INA219 over I2C on a Raspberry Pi runtime.</summary>
    Ina219,
}

/// <summary>
/// Composition-root registration for the battery runtime
/// (specs/hardware/battery-monitoring.md). Unknown or incompatible
/// configurations abort startup loudly — nothing ever silently degrades from a
/// real sensor to the simulated value (decision D5). <c>mock</c> registers the
/// deterministic simulator and never reads the <c>Battery:</c> section;
/// <c>ina219</c> passes the Raspberry platform gate (OS facts only) and
/// requires a structurally valid <c>Battery:</c> configuration.
/// </summary>
public static class BatteryRuntimeSelection
{
    /// <summary>Configuration key selecting the battery mode (env: BATTERY_MODE).</summary>
    public const string BatteryModeKey = "BATTERY_MODE";

    /// <summary>
    /// Registers the mode-appropriate battery runtime. Throws
    /// <see cref="HostAbortedException"/> for rejected or incompatible modes.
    /// </summary>
    /// <returns>The resolved mode, so the host can log the honest posture.</returns>
    public static BatteryMode AddBatteryRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddBatteryRuntime(configuration, IsRaspberryRuntime());

    /// <summary>
    /// Core registration with an injected platform-preflight result (the
    /// internal seam lets composition tests exercise the <c>ina219</c> graph on
    /// a development PC without weakening the production preflight —
    /// test-only, exposed to DroneControl.Api.Tests via InternalsVisibleTo).
    /// </summary>
    internal static BatteryMode AddBatteryRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        bool raspberryRuntimeSupported)
    {
        var mode = ParseBatteryMode(configuration[BatteryModeKey]);
        if (mode == BatteryMode.Mock)
        {
            // mock: the Battery: section is never read (mirrors how the mock
            // hardware/camera modes skip their sections). Descriptor-identical
            // to an absent key, including when an invalid Battery value happens
            // to be present.
            services.AddSingleton<IBatteryMonitor, MockBatteryMonitor>();
            return mode;
        }

        RequireBatteryPlatform(raspberryRuntimeSupported);

        BatteryOptions options;
        try
        {
            options = BatteryOptions.FromSection(ReadBatterySectionValues(configuration));
        }
        catch (ArgumentException exception)
        {
            throw new HostAbortedException(
                $"BATTERY_MODE 'ina219' has invalid Battery configuration: {exception.Message}");
        }

        services.AddSingleton(options);
        services.AddSingleton<II2cBus>(_ => new RaspberryI2cBus(options.I2cBusPath));
        services.AddSingleton<IBatteryMonitor>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<Ina219BatteryMonitor>>();
            return new Ina219BatteryMonitor(
                provider.GetRequiredService<II2cBus>(),
                options,
                exception => logger.LogWarning(
                    exception,
                    "Battery sensor read failed on {BusPath} address 0x{Address:X2}; reporting unavailable.",
                    options.I2cBusPath,
                    options.I2cAddress));
        });
        return mode;
    }

    private static BatteryMode ParseBatteryMode(string? raw)
    {
        // Unset (null) is the only path to the default; a present-but-empty
        // value is as invalid as any unknown string (same rule as HARDWARE_MODE).
        if (raw is null)
        {
            return BatteryMode.Mock;
        }

        var value = raw.Trim();
        return value switch
        {
            _ when value.Equals("mock", StringComparison.OrdinalIgnoreCase) => BatteryMode.Mock,
            _ when value.Equals("ina219", StringComparison.OrdinalIgnoreCase) => BatteryMode.Ina219,
            _ => throw new HostAbortedException(
                $"BATTERY_MODE value '{raw}' is invalid. Accepted values: mock, ina219 (case-insensitive)."),
        };
    }

    private static bool IsRaspberryRuntime()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64;

    private static void RequireBatteryPlatform(bool raspberryRuntimeSupported)
    {
        if (raspberryRuntimeSupported)
        {
            return;
        }

        var observed = $"{RuntimeInformation.RuntimeIdentifier}";
        throw new HostAbortedException(
            $"BATTERY_MODE 'ina219' requires a Raspberry Pi runtime (linux/arm64); " +
            $"this process reports '{observed}'. Use 'mock' on a development PC.");
    }

    /// <summary>
    /// Flattens the (flat) <c>Battery:</c> section into section-relative
    /// key/value pairs for <see cref="BatteryOptions.FromSection"/>, which
    /// deliberately carries no Microsoft.Extensions.Configuration dependency.
    /// </summary>
    private static Dictionary<string, string?> ReadBatterySectionValues(IConfiguration configuration)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection("Battery").GetChildren())
        {
            values[child.Key] = child.Value;
        }

        return values;
    }
}
