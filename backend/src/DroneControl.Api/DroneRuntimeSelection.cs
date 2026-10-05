using System.Runtime.InteropServices;
using DroneControl.Application;
using DroneControl.Infrastructure;

namespace DroneControl.Api;

/// <summary>
/// The runtime hardware mode selected from configuration (decision D3/D5 of
/// specs/architecture/runtime-deployment.md). Selection lives in the
/// composition root — the only platform-aware spot in the system. The mode is
/// read once at startup; nothing can switch modes while the process runs.
/// </summary>
public enum HardwareMode
{
    /// <summary>Simulated drone side (default; today's exact graph).</summary>
    Mock,

    /// <summary>
    /// Raspberry runtime with every GPIO/PWM output recorded and suppressed at
    /// the dry-run output boundary (specs/hardware/dry-run.md).
    /// </summary>
    DryRun,

    /// <summary>Real hardware via the Raspberry provider.</summary>
    Real,
}

/// <summary>
/// Composition-root registration for drone runtime modes
/// (specs/hardware/raspberry-hardware-provider.md and
/// specs/hardware/dry-run.md). Unknown or incompatible configurations abort
/// startup loudly — nothing ever silently degrades from one mode to another
/// (decision D5). Hardware-backed modes pass the supported Raspberry runtime
/// preflight (default target: linux/arm64); this gate only checks OS facts.
/// </summary>
public static class DroneRuntimeSelection
{
    /// <summary>Configuration key selecting the hardware mode (env: HARDWARE_MODE).</summary>
    public const string HardwareModeKey = "HARDWARE_MODE";

    /// <summary>
    /// Default GPIO character-device chip for real mode. This value was
    /// target-detected in docs/hardware/raspberry-pi-inventory.md
    /// (`/dev/gpiochip0`), not invented; an operator may override it via
    /// `Raspberry:GPIO:ChipPath`.
    /// </summary>
    private const string DefaultGpioChipPath = "/dev/gpiochip0";

    /// <summary>
    /// Registers the mode-appropriate implementations. Throws
    /// <see cref="HostAbortedException"/> for rejected or incompatible modes.
    /// </summary>
    /// <returns>The resolved mode, so the host can log the honest posture on supported hardware.</returns>
    public static HardwareMode AddDroneRuntime(this IServiceCollection services, IConfiguration configuration)
        => services.AddDroneRuntime(configuration, IsRaspberryRuntime());

    /// <summary>
    /// Core registration with an injected platform-preflight result. The
    /// public entry point always passes the real OS/architecture check; the
    /// internal seam lets composition tests resolve the hardware-backed graphs
    /// on a development PC without weakening production preflight (test-only,
    /// exposed to DroneControl.Api.Tests via InternalsVisibleTo).
    /// </summary>
    internal static HardwareMode AddDroneRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        bool raspberryRuntimeSupported)
    {
        var mode = ParseMode(configuration[HardwareModeKey]);

        switch (mode)
        {
            case HardwareMode.Mock:
                // Today's exact graph: the simulator owns the session state,
                // so it is a singleton (Task 11 rationale, unchanged).
                // Program.cs wraps this concrete inner controller with the
                // fail-safe decorator (Task 26).
                services.AddSingleton<MockDroneController>();
                break;

            case HardwareMode.DryRun:
                RequireRaspberryPlatform(mode, raspberryRuntimeSupported);
                RegisterHardwareFlow(services, configuration, includeDryRunOutputBoundary: true);
                break;

            case HardwareMode.Real:
                RequireRaspberryPlatform(mode, raspberryRuntimeSupported);
                RegisterHardwareFlow(services, configuration, includeDryRunOutputBoundary: false);
                break;
        }

        return mode;
    }

    /// <summary>
    /// The shared hardware-backed command flow used by both <c>dry-run</c>
    /// and <c>real</c>: same provider, same bridge, same controller. Only the
    /// final output sinks differ — dry-run adds the suppressing GPIO/PWM
    /// sinks and structurally excludes the real ones.
    /// </summary>
    private static void RegisterHardwareFlow(
        IServiceCollection services,
        IConfiguration configuration,
        bool includeDryRunOutputBoundary)
    {
        var modeName = includeDryRunOutputBoundary ? "dry-run" : "real";

        // Task 29 (specs/hardware/motor-control.md activation matrix): the
        // MotorMapping section triggers STRICT validation in both hardware
        // modes; an absent section keeps today's inert provider untouched.
        // Validation happens at composition time — an invalid mapping aborts
        // startup with the full error list, never a silent inert/mock
        // fallback (decision D5).
        GpioPinConfiguration? gpioConfiguration = null;
        MotorMapping? motorMapping = null;
        var mappingSection = configuration.GetSection("MotorMapping");
        if (mappingSection.Exists())
        {
            // Coverage is validated against the bound digital identifiers, so
            // the GPIO configuration must exist whenever a mapping does.
            gpioConfiguration = ReadGpioConfiguration(configuration, modeName);

            try
            {
                motorMapping = MotorMapping.FromSection(
                    FlattenSectionValues(mappingSection),
                    gpioConfiguration);
            }
            catch (ArgumentException exception)
            {
                throw new HostAbortedException(
                    $"HARDWARE_MODE '{modeName}' has an invalid MotorMapping section: {exception.Message}");
            }

            if (motorMapping.DirectionMappingVerified)
            {
                // Asserted: kept — the real/dry-run output sink is registered
                // below and the provider reports the direction capability.
            }
            else
            {
                // Matrix row "present, valid | false": validated but unused —
                // identical legacy inert behavior.
                motorMapping = null;
            }
        }

        // Task 30 (specs/hardware/pwm.md activation matrix): the PwmMapping
        // section triggers STRICT validation in both hardware modes — flag or
        // not, valid content is mandatory the moment the section exists; an
        // absent section keeps speed absent. Validation happens at
        // composition time — an invalid envelope aborts startup with the full
        // error list, never a silent inert fallback (decision D5).
        PwmMapping? pwmMapping = null;
        var pwmSection = configuration.GetSection("PwmMapping");
        if (pwmSection.Exists())
        {
            // The PWM identifiers live in the GPIO section, so its validated
            // configuration must exist whenever a PwmMapping does.
            gpioConfiguration ??= ReadGpioConfiguration(configuration, modeName);

            try
            {
                // Null when valid but unasserted ("validated but unused").
                pwmMapping = PwmMapping.FromSection(
                    FlattenSectionValues(pwmSection),
                    gpioConfiguration,
                    ReadGpioSectionValues(configuration));
            }
            catch (ArgumentException exception)
            {
                throw new HostAbortedException(
                    $"HARDWARE_MODE '{modeName}' has an invalid PwmMapping section: {exception.Message}");
            }
        }

        // Real-mode PWM config validation happens BEFORE any graph
        // registration (D5): an asserted envelope without operator PWM
        // evidence aborts naming the missing Raspberry:PWM configuration,
        // leaving no partial graph behind.
        string? realPwmChipPath = null;
        IReadOnlyDictionary<int, int>? realPwmChannels = null;
        if (!includeDryRunOutputBoundary && pwmMapping is not null)
        {
            realPwmChipPath = configuration["Raspberry:PWM:ChipPath"];
            if (string.IsNullOrWhiteSpace(realPwmChipPath))
            {
                throw new HostAbortedException(
                    "HARDWARE_MODE 'real' with an asserted PwmMapping requires the concrete PWM sysfs chip " +
                    "path: set Raspberry:PWM:ChipPath from the target's /sys/class/pwm/pwmchipN evidence " +
                    "(docs/hardware/raspberry-pi-inventory.md). Refusing to start with an unconfigured real sink.");
            }

            realPwmChannels = ReadPwmChannels(configuration, pwmMapping.Identifiers);
        }

        // Atomic pair (Development ValidateOnBuild constructs the graph):
        // the hardware-backed controller consumes the provider. Each active
        // capability is passed explicitly — GetService returns null when its
        // controller was never registered — and both null reproduces the
        // inert provider exactly. A DI constructor choice cannot express
        // speed-only, so the composition root picks the constructor itself.
        services.AddSingleton<IDroneHardware>(provider => new RaspberryDroneHardware(
            provider.GetService<MotorController>(),
            provider.GetService<SpeedController>()));
        services.AddSingleton<HardwareDroneController>();

        if (!includeDryRunOutputBoundary)
        {
            // Real mode: the concrete Linux output sinks (IRaspberryGpioPlatform
            // -> RaspberryGpioController, IRaspberryPwmPlatform ->
            // RaspberryPwmController) are registered only here — never in
            // mock/dry-run — and only when the matching mapping is asserted
            // (an unasserted real graph stays exactly inert, as before).
            // motorMapping non-null guarantees gpioConfiguration was bound
            // above (any mapping section requires valid GPIO configuration).
            RegisterRealOutputSinks(services, configuration, gpioConfiguration!, motorMapping, pwmMapping, realPwmChipPath, realPwmChannels);
            return;
        }

        // Unsafe composition guard: a dry-run graph must never contain a real
        // output sink. Fail startup loudly instead of trusting a runtime flag.
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(IRaspberryGpioPlatform)
                || descriptor.ServiceType == typeof(RaspberryGpioController)
                || descriptor.ImplementationType == typeof(RaspberryGpioController)
                || descriptor.ServiceType == typeof(IRaspberryPwmPlatform)
                || descriptor.ServiceType == typeof(RaspberryPwmController)
                || descriptor.ImplementationType == typeof(RaspberryPwmController)))
        {
            throw new HostAbortedException(
                "HARDWARE_MODE 'dry-run' selected but the service graph already contains a real " +
                "output sink (RaspberryGpioController/IRaspberryGpioPlatform or " +
                "RaspberryPwmController/IRaspberryPwmPlatform). " +
                "Unsafe composition: refusing to start.");
        }

        // Dry-run configuration preflight: missing/invalid GPIO values abort
        // startup here, before any command can be accepted — never a silent
        // fallback to mock or real (specs/hardware/dry-run.md Error Cases).
        gpioConfiguration ??= ReadGpioConfiguration(configuration, modeName);

        services.AddSingleton<IGpioController>(provider =>
        {
            var logger = provider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HardwareDryRun");
            return new DryRunGpioController(gpioConfiguration, record => logger.LogInformation(
                "Dry-run hardware output recorded and suppressed: Mode={Mode} Category={Category} " +
                "Sequence={Sequence} Identifier={Identifier} Parameters={Parameters} " +
                "DryRun={DryRun} PhysicallyApplied={PhysicallyApplied} Reason={Reason}",
                "dry-run",
                record.Category,
                record.Sequence,
                record.Identifier,
                record.RequestedParameters,
                record.DryRun,
                record.PhysicallyApplied,
                record.SuppressionReason));
        });

        if (motorMapping is not null)
        {
            // Active mapping (verified assertion): MotorController consumes the
            // dry-run sink above — mode-agnostic upstream, suppressed output.
            // The provider factory picks up this registration and reports the
            // direction capability.
            var mapping = motorMapping;
            services.AddSingleton(mapping);
            services.AddSingleton(provider => new MotorController(
                provider.GetRequiredService<IGpioController>(),
                mapping));
        }

        if (pwmMapping is not null)
        {
            // Active envelope (verified assertion): SpeedController consumes
            // the dry-run PWM sink below — mode-agnostic upstream, suppressed
            // output. The provider factory picks up this registration and
            // reports the speed capability.
            var envelope = pwmMapping;
            services.AddSingleton(envelope);
            services.AddSingleton<IPwmController>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HardwareDryRun");
                return new DryRunPwmController(envelope.Identifiers, record => logger.LogInformation(
                    "Dry-run hardware output recorded and suppressed: Mode={Mode} Category={Category} " +
                    "Sequence={Sequence} Identifier={Identifier} Parameters={Parameters} " +
                    "DryRun={DryRun} PhysicallyApplied={PhysicallyApplied} Reason={Reason}",
                    "dry-run",
                    record.Category,
                    record.Sequence,
                    record.Identifier,
                    record.RequestedParameters,
                    record.DryRun,
                    record.PhysicallyApplied,
                    record.SuppressionReason));
            });
            services.AddSingleton(provider => new SpeedController(
                provider.GetRequiredService<IPwmController>(),
                envelope));
        }
    }

    /// <summary>
    /// Real-mode output sinks (specs/hardware/raspberry-hardware-provider.md +
    /// concrete Linux platforms). The concrete <see cref="RaspberryGpioPlatform"/>
    /// (GPIO character device) and <see cref="RaspberryPwmPlatform"/> (PWM
    /// sysfs) are wired into the adapters and only register in
    /// <c>HARDWARE_MODE=real</c>. GPIO chip access defaults to the
    /// inventory-detected <c>/dev/gpiochip0</c>; PWM requires operator
    /// evidence (chip path + per-identifier channels) and aborts loudly with
    /// the missing config named when asserted but unconfigured — a real sink
    /// is never served inert or with guessed channels (D5).
    /// </summary>
    private static void RegisterRealOutputSinks(
        IServiceCollection services,
        IConfiguration configuration,
        GpioPinConfiguration gpioConfiguration,
        MotorMapping? motorMapping,
        PwmMapping? pwmMapping,
        string? realPwmChipPath,
        IReadOnlyDictionary<int, int>? realPwmChannels)
    {
        if (motorMapping is not null)
        {
            var chipPath = configuration["Raspberry:GPIO:ChipPath"] ?? DefaultGpioChipPath;
            var mapping = motorMapping;
            services.AddSingleton<IRaspberryGpioPlatform>(_ => new RaspberryGpioPlatform(chipPath));
            services.AddSingleton<IGpioController>(provider => new RaspberryGpioController(
                gpioConfiguration,
                provider.GetRequiredService<IRaspberryGpioPlatform>()));
            services.AddSingleton(mapping);
            services.AddSingleton(provider => new MotorController(
                provider.GetRequiredService<IGpioController>(),
                mapping));
        }

        if (pwmMapping is not null)
        {
            // Pre-validated before any graph registration (D5): chip path and
            // per-identifier channels are non-null here.
            var chipPath = realPwmChipPath!;
            var channels = realPwmChannels!;
            var envelope = pwmMapping;
            services.AddSingleton<IRaspberryPwmPlatform>(_ => new RaspberryPwmPlatform(chipPath, channels));
            services.AddSingleton<IPwmController>(provider => new RaspberryPwmController(
                envelope.Identifiers,
                provider.GetRequiredService<IRaspberryPwmPlatform>()));
            services.AddSingleton(envelope);
            services.AddSingleton(provider => new SpeedController(
                provider.GetRequiredService<IPwmController>(),
                envelope));
        }
    }

    /// <summary>
    /// Reads the operator-supplied PWM identifier→channel mapping
    /// (<c>Raspberry:PWM:Channels:&lt;identifier&gt;</c>). Every asserted
    /// identifier must be mapped from target evidence; a missing mapping aborts
    /// (D5) — a real PWM output must never write to an invented channel.
    /// </summary>
    private static Dictionary<int, int> ReadPwmChannels(
        IConfiguration configuration,
        IEnumerable<int> identifiers)
    {
        var channels = new Dictionary<int, int>();
        foreach (var identifier in identifiers)
        {
            var raw = configuration[$"Raspberry:PWM:Channels:{identifier}"];
            if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw, out var channel) || channel < 0)
            {
                throw new HostAbortedException(
                    $"HARDWARE_MODE 'real' requires the PWM channel for identifier {identifier}: set " +
                    $"Raspberry:PWM:Channels:{identifier} from the target's PWM sysfs evidence. " +
                    "Refusing to start with an unconfigured real sink.");
            }

            channels.Add(identifier, channel);
        }

        return channels;
    }

    /// <summary>
    /// Binds the GPIO section with the established fail-fast contract
    /// (missing/malformed values abort startup with an explicit message).
    /// </summary>
    private static GpioPinConfiguration ReadGpioConfiguration(IConfiguration configuration, string modeName)
    {
        var gpioValues = ReadGpioSectionValues(configuration);

        try
        {
            return GpioPinConfiguration.FromSection(gpioValues);
        }
        catch (ArgumentException exception)
        {
            throw new HostAbortedException(
                $"HARDWARE_MODE '{modeName}' requires valid GPIO configuration: {exception.Message}");
        }
    }

    /// <summary>
    /// Raw key/value pairs of the GPIO section (used by the GPIO binding and
    /// by the PwmMapping binding, which reads the PWM1/PWM2 identifiers).
    /// </summary>
    private static Dictionary<string, string?> ReadGpioSectionValues(IConfiguration configuration)
        => configuration.GetSection("GPIO")
            .GetChildren()
            .ToDictionary(child => child.Key, child => (string?)child.Value);

    /// <summary>
    /// Flattens a configuration section into section-relative keys (for
    /// example <c>Commands:forward:Pin1</c>) for
    /// <see cref="MotorMapping.FromSection"/>, which deliberately carries no
    /// Microsoft.Extensions.Configuration dependency. A present-but-empty node
    /// flattens to a null value so validation reports it instead of skipping
    /// it. Key casing is preserved as read; lookups are case-insensitive.
    /// </summary>
    private static Dictionary<string, string?> FlattenSectionValues(IConfigurationSection section)
    {
        var flat = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        void Walk(IConfigurationSection node, string prefix)
        {
            foreach (var child in node.GetChildren())
            {
                var key = prefix.Length == 0 ? child.Key : $"{prefix}:{child.Key}";
                if (child.Value is not null)
                {
                    flat[key] = child.Value;
                }

                if (child.GetChildren().Any())
                {
                    Walk(child, key);
                }
                else if (child.Value is null)
                {
                    flat[key] = null;
                }
            }
        }

        Walk(section, string.Empty);
        return flat;
    }

    private static HardwareMode ParseMode(string? raw)
    {
        // Unset (null) is the only path to the default; a present-but-empty
        // value is as invalid as any unknown string.
        if (raw is null)
        {
            return HardwareMode.Mock;
        }

        var value = raw.Trim();
        return value switch
        {
            _ when value.Equals("mock", StringComparison.OrdinalIgnoreCase) => HardwareMode.Mock,
            _ when value.Equals("dry-run", StringComparison.OrdinalIgnoreCase) => HardwareMode.DryRun,
            _ when value.Equals("real", StringComparison.OrdinalIgnoreCase) => HardwareMode.Real,
            _ => throw new HostAbortedException(
                $"HARDWARE_MODE value '{raw}' is invalid. Accepted values: mock, dry-run, real (case-insensitive)."),
        };
    }

    private static bool IsRaspberryRuntime()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64;

    private static void RequireRaspberryPlatform(HardwareMode mode, bool raspberryRuntimeSupported)
    {
        if (raspberryRuntimeSupported)
        {
            return;
        }

        var modeName = mode == HardwareMode.DryRun ? "dry-run" : "real";
        var observed = $"{RuntimeInformation.RuntimeIdentifier}";
        throw new HostAbortedException(
            $"HARDWARE_MODE '{modeName}' requires a Raspberry Pi runtime (linux/arm64); " +
            $"this process reports '{observed}'. Use 'mock' on a development PC.");
    }
}
