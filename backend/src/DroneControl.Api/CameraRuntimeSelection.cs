using System.Runtime.InteropServices;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Api;

/// <summary>
/// The camera runtime mode selected from configuration (decisions D3/D5 of
/// specs/architecture/runtime-deployment.md, implemented by
/// specs/hardware/camera-runtime.md). Selection lives in the composition root;
/// the mode is read once at startup and nothing can switch it while the
/// process runs.
/// </summary>
public enum CameraMode
{
    /// <summary>Simulated camera status — today's exact behavior (default).</summary>
    Mock,

    /// <summary>
    /// Real pipeline: camera status comes from the probe-backed monitor
    /// (endpoint reachability, never verified video).
    /// </summary>
    Ustreamer,
}

/// <summary>
/// Composition-root registration for the camera runtime
/// (specs/hardware/camera-runtime.md). Unknown or incompatible configurations
/// abort startup loudly — nothing ever silently degrades from `ustreamer` to
/// `mock` (decision D5). `mock` registers nothing camera-related: the mock
/// graph and its simulated camera behavior stay byte-identical, and the
/// `Camera:` section is never read. `ustreamer` additionally passes the
/// Raspberry platform gate (OS facts only) and requires a structurally valid
/// `Camera:ProbeUrl`; reachability is intentionally NOT checked at startup —
/// the control plane must never depend on the camera (decision D4).
/// </summary>
public static class CameraRuntimeSelection
{
    /// <summary>Configuration key selecting the camera mode (env: CAMERA_MODE).</summary>
    public const string CameraModeKey = "CAMERA_MODE";

    /// <summary>Configuration key holding the absolute probe URL (env: CAMERA__PROBEURL).</summary>
    public const string ProbeUrlKey = "Camera:ProbeUrl";

    /// <summary>Named <see cref="HttpClient"/> used for the headers-only probe.</summary>
    public const string ProbeClientName = "camera-probe";

    /// <summary>
    /// Registers the mode-appropriate camera runtime. Throws
    /// <see cref="HostAbortedException"/> for rejected or incompatible modes.
    /// </summary>
    /// <returns>The resolved mode, so the host can log the honest posture.</returns>
    public static CameraMode AddCameraRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddCameraRuntime(configuration, IsRaspberryRuntime());

    /// <summary>
    /// Core registration with an injected platform-preflight result (the
    /// internal seam lets composition tests exercise the `ustreamer` graph on
    /// a development PC without weakening the production preflight —
    /// test-only, exposed to DroneControl.Api.Tests via InternalsVisibleTo).
    /// </summary>
    internal static CameraMode AddCameraRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        bool raspberryRuntimeSupported)
    {
        var mode = ParseCameraMode(configuration[CameraModeKey]);
        if (mode == CameraMode.Mock)
        {
            // mock: the Camera: section is never read (mirrors how the mock
            // hardware mode skips MotorMapping/PwmMapping), and registrations
            // are identical to an absent key — including when an invalid
            // Camera:ProbeUrl happens to be present.
            return mode;
        }

        RequireCameraPlatform(raspberryRuntimeSupported);
        var probeUrl = ValidateProbeUrl(configuration[ProbeUrlKey]);

        services.AddHttpClient(ProbeClientName);
        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(ProbeClientName);
            var logger = serviceProvider.GetRequiredService<ILogger<CameraStatusMonitor>>();
            return new CameraStatusMonitor(probeUrl, httpClient, (previous, current) =>
            {
                // The monitor notifies on transitions only, so repeated
                // failures of the same state never repeat-log.
                if (current == CameraStatus.Streaming)
                {
                    logger.LogInformation(
                        "Camera stream endpoint became reachable ({PreviousCameraStatus} -> {CameraStatus}); " +
                        "reachability is endpoint health, never verified video.",
                        previous,
                        current);
                }
                else if (current == CameraStatus.Error)
                {
                    logger.LogWarning(
                        "Camera stream endpoint probe failed ({PreviousCameraStatus} -> {CameraStatus}).",
                        previous,
                        current);
                }
            });
        });
        services.AddHostedService<CameraMonitorHostedService>();
        return mode;
    }

    /// <summary>
    /// Applies the camera overlay between the inner controller and the safety
    /// decorator (decision: safety must stay outermost so its closed-path
    /// `Camera = Offline` override wins). In `mock` mode the inner controller
    /// is returned untouched — the class does not exist in the graph.
    /// </summary>
    internal static IDroneController ApplyCameraOverlay(
        IDroneController inner,
        CameraMode mode,
        IServiceProvider services)
        => mode == CameraMode.Ustreamer
            ? new CameraStatusOverlay(
                inner,
                services.GetRequiredService<CameraStatusMonitor>())
            : inner;

    private static CameraMode ParseCameraMode(string? raw)
    {
        // Unset (null) is the only path to the default; a present-but-empty
        // value is as invalid as any unknown string (same rule as HARDWARE_MODE).
        if (raw is null)
        {
            return CameraMode.Mock;
        }

        var value = raw.Trim();
        return value switch
        {
            _ when value.Equals("mock", StringComparison.OrdinalIgnoreCase) => CameraMode.Mock,
            _ when value.Equals("ustreamer", StringComparison.OrdinalIgnoreCase) => CameraMode.Ustreamer,
            _ => throw new HostAbortedException(
                $"CAMERA_MODE value '{raw}' is invalid. Accepted values: mock, ustreamer (case-insensitive)."),
        };
    }

    private static bool IsRaspberryRuntime()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64;

    private static void RequireCameraPlatform(bool raspberryRuntimeSupported)
    {
        if (raspberryRuntimeSupported)
        {
            return;
        }

        var observed = $"{RuntimeInformation.RuntimeIdentifier}";
        throw new HostAbortedException(
            $"CAMERA_MODE 'ustreamer' requires a Raspberry Pi runtime (linux/arm64); " +
            $"this process reports '{observed}'. Use 'mock' on a development PC.");
    }

    private static string ValidateProbeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new HostAbortedException(
                $"CAMERA_MODE 'ustreamer' requires a {ProbeUrlKey} value " +
                "(absolute http/https URL of the public camera path).");
        }

        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new HostAbortedException(
                $"{ProbeUrlKey} value '{raw}' is invalid. Expected an absolute http:// or https:// URL.");
        }

        return trimmed;
    }
}
