using System.Net;
using DroneControl.Api;
using DroneControl.Application;
using DroneControl.Domain;
using DroneControl.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DroneControl.Api.Tests;

/// <summary>
/// Composition-root camera mode selection tests
/// (specs/hardware/camera-runtime.md): strict CAMERA_MODE parsing, the
/// linux/arm64 gate, the Camera:ProbeUrl contract, the registration matrix
/// (mock registers nothing), the below-safety overlay composition, and the
/// shipped configuration.
/// </summary>
public class CameraRuntimeCompositionTests
{
    // --- CAMERA_MODE parsing ---

    [Fact]
    public void UnsetCameraMode_IsMock_AndRegistersNothingCameraRelated()
    {
        var services = new ServiceCollection();

        // No camera key at all — even on a platform that cannot host
        // ustreamer, mock is always valid.
        var mode = services.AddCameraRuntime(Config(), raspberryRuntimeSupported: false);

        Assert.Equal(CameraMode.Mock, mode);
        Assert.False(HasCameraRegistrations(services));
    }

    [Fact]
    public void ExplicitMock_MatchesUnset_AndIgnoresTheCameraSectionEntirely()
    {
        var unset = new ServiceCollection();
        var explicitMock = new ServiceCollection();

        var unsetMode = unset.AddCameraRuntime(Config(), raspberryRuntimeSupported: false);

        // An invalid Camera:ProbeUrl next to mock must be ignored: the
        // section is never read in mock mode.
        var mode = explicitMock.AddCameraRuntime(
            Config(("CAMERA_MODE", "mock"), ("Camera:ProbeUrl", "not a url")),
            raspberryRuntimeSupported: false);

        Assert.Equal(CameraMode.Mock, unsetMode);
        Assert.Equal(CameraMode.Mock, mode);

        // Descriptor-for-descriptor identical to an absent key.
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

        var mode = services.AddCameraRuntime(
            Config(("CAMERA_MODE", value)),
            raspberryRuntimeSupported: false);

        Assert.Equal(CameraMode.Mock, mode);
        Assert.False(HasCameraRegistrations(services));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sometimes")]
    public void InvalidCameraMode_AbortsNamingTheKeyAndAcceptedValues(string value)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddCameraRuntime(Config(("CAMERA_MODE", value)), raspberryRuntimeSupported: false));

        Assert.Contains("CAMERA_MODE", ex.Message);
        Assert.Contains($"'{value}'", ex.Message);
        Assert.Contains("mock, ustreamer (case-insensitive)", ex.Message);
    }

    // --- Platform gate ---

    [Fact]
    public void Ustreamer_OnUnsupportedPlatform_AbortsNamingModeAndRequirement()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddCameraRuntime(
                Config(
                    ("CAMERA_MODE", "ustreamer"),
                    ("Camera:ProbeUrl", "http://frontend/camera/")),
                raspberryRuntimeSupported: false));

        Assert.Contains("'ustreamer'", ex.Message);
        Assert.Contains("linux/arm64", ex.Message);
        Assert.Contains("Use 'mock' on a development PC.", ex.Message);
        Assert.False(HasCameraRegistrations(services));
    }

    [Fact]
    public void UstreamerOnSupportedPlatform_AcceptsTrimmedCaseInsensitiveValue()
    {
        var services = new ServiceCollection();

        var mode = services.AddCameraRuntime(
            Config(
                ("CAMERA_MODE", "  USTREAMER  "),
                ("Camera:ProbeUrl", "http://frontend/camera/")),
            raspberryRuntimeSupported: true);

        Assert.Equal(CameraMode.Ustreamer, mode);
        Assert.True(HasCameraRegistrations(services));
    }

    // --- Camera:ProbeUrl contract ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ustreamer_WithoutProbeUrl_AbortsNamingTheKey(string? value)
    {
        var services = new ServiceCollection();
        var values = new List<(string Key, string Value)> { ("CAMERA_MODE", "ustreamer") };
        if (value is not null)
        {
            values.Add(("Camera:ProbeUrl", value));
        }

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddCameraRuntime(Config([.. values]), raspberryRuntimeSupported: true));

        Assert.Contains("requires a Camera:ProbeUrl", ex.Message);
        Assert.False(HasCameraRegistrations(services));
    }

    [Theory]
    [InlineData("frontend/camera")]
    [InlineData("camera.test/camera")]
    [InlineData("ftp://camera.local/stream")]
    public void Ustreamer_WithNonHttpAbsoluteProbeUrl_Aborts(string value)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<HostAbortedException>(
            () => services.AddCameraRuntime(
                Config(("CAMERA_MODE", "ustreamer"), ("Camera:ProbeUrl", value)),
                raspberryRuntimeSupported: true));

        Assert.Contains("Expected an absolute http:// or https:// URL", ex.Message);
        Assert.False(HasCameraRegistrations(services));
    }

    // --- Registration matrix ---

    [Fact]
    public void Ustreamer_ValidConfig_RegistersMonitorHostedLoopAndProbeClient()
    {
        var services = new ServiceCollection();

        var mode = services.AddCameraRuntime(
            Config(
                ("CAMERA_MODE", "ustreamer"),
                ("Camera:ProbeUrl", "http://frontend/camera/")),
            raspberryRuntimeSupported: true);

        Assert.Equal(CameraMode.Ustreamer, mode);
        Assert.Contains(services, s => s.ServiceType == typeof(CameraStatusMonitor));
        Assert.Contains(
            services,
            s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType == typeof(CameraMonitorHostedService));
        Assert.Contains(services, s => s.ServiceType == typeof(IHttpClientFactory));
    }

    // --- Overlay composition ---

    [Fact]
    public void ApplyCameraOverlay_InMockMode_ReturnsTheInnerControllerUntouched()
    {
        var inner = new StubController();
        var services = new ServiceCollection();

        var result = CameraRuntimeSelection.ApplyCameraOverlay(
            inner,
            CameraMode.Mock,
            services.BuildServiceProvider());

        Assert.Same(inner, result);
    }

    [Fact]
    public async Task ApplyCameraOverlay_InUstreamerMode_WrapsAndReportsTheMonitorCache()
    {
        var monitor = CreateMonitorWithStatus(CameraStatus.Streaming);
        var services = new ServiceCollection();
        services.AddSingleton(monitor);
        var inner = new StubController
        {
            NextStatus = new DroneStatus
            {
                State = new DroneState
                {
                    Connection = ConnectionStatus.Connected,
                    Camera = CameraStatus.Offline,
                },
            },
        };

        var composed = CameraRuntimeSelection.ApplyCameraOverlay(
            inner,
            CameraMode.Ustreamer,
            services.BuildServiceProvider());

        var overlay = Assert.IsType<CameraStatusOverlay>(composed);
        Assert.NotSame(inner, overlay);

        var status = await composed.GetStatusAsync();
        Assert.Equal(CameraStatus.Streaming, status.State.Camera);
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
    }

    [Fact]
    public async Task SafetyClosedOverride_WinsOverAStreamingMonitor()
    {
        // Probe observed a healthy endpoint...
        var monitor = CreateMonitorWithStatus(CameraStatus.Streaming);
        var services = new ServiceCollection();
        services.AddSingleton(monitor);
        var inner = new StubController();
        var composed = CameraRuntimeSelection.ApplyCameraOverlay(
            inner,
            CameraMode.Ustreamer,
            services.BuildServiceProvider());

        // ...but the safety path starts closed: real mode without the D7
        // external-protection assertion faults before any controller call.
        var safety = new DroneSafetyController(
            composed,
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 200,
                stopTimeoutMilliseconds: 100,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0,
                realHardwareMode: true,
                externalAbruptFailureProtectionVerified: false),
            _ => { });

        await safety.StartSafetyAsync();
        Assert.Equal(DroneSafetyState.Faulted, safety.CurrentSafetyState);

        // The Task 26 override is composed above the overlay: camera is
        // forced Offline even though the monitor reports Streaming.
        var status = await safety.GetStatusAsync();
        Assert.Equal(CameraStatus.Offline, status.State.Camera);
    }

    // --- Control independence (scenario 8) ---

    [Fact]
    public async Task WithAFailingProbe_StatusReadsErrorWhileControlFlowPassesThroughUnchanged()
    {
        // One immediate headers-only attempt against a refusing target maps to
        // Error; no loop, no wall-clock wait.
        var monitor = new CameraStatusMonitor(
            "http://frontend/camera/",
            new HttpClient(new RefusingHandler()),
            onTransition: null,
            probeTimeout: TimeSpan.FromMilliseconds(200),
            delay: static cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        await monitor.ProbeOnceAsync(CancellationToken.None);
        Assert.Equal(CameraStatus.Error, monitor.Current);

        var services = new ServiceCollection();
        services.AddSingleton(monitor);
        var inner = new StubController();
        var composed = CameraRuntimeSelection.ApplyCameraOverlay(
            inner,
            CameraMode.Ustreamer,
            services.BuildServiceProvider());
        var safety = new DroneSafetyController(
            composed,
            new DroneSafetyOptions(
                commandTimeoutMilliseconds: 200,
                stopTimeoutMilliseconds: 100,
                stopRetryCount: 0,
                stopRetryDelayMilliseconds: 0,
                recoveryRetryCount: 0,
                recoveryRetryDelayMilliseconds: 0),
            _ => { });

        await safety.StartSafetyAsync();
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);

        // Status reads surface the camera Error promptly...
        var status = await safety.GetStatusAsync();
        Assert.Equal(CameraStatus.Error, status.State.Camera);
        Assert.Equal(CameraStatus.Error, monitor.Current);

        // ...while the control plane is untouched by the camera path: no
        // exception from the probe pipeline ever surfaces on an API call.
        await safety.ConnectAsync();
        await safety.SendCommandAsync(DroneCommand.Forward);
        await safety.SetSpeedAsync(50);
        await safety.SendCommandAsync(DroneCommand.Stop);

        Assert.Contains("connect", inner.Calls);
        Assert.Contains("command:Forward", inner.Calls);
        Assert.Contains("speed:50", inner.Calls);
        Assert.Equal(DroneSafetyState.Safe, safety.CurrentSafetyState);
    }

    // --- Shipped configuration ---

    [Fact]
    public void ShippedConfiguration_ContainsNoCameraSection_AndKeepsTheD7DefaultFalse()
    {
        var shipped = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Assert.DoesNotContain("CAMERA_MODE", shipped);
        Assert.DoesNotContain("Camera", shipped);
        Assert.DoesNotContain("ProbeUrl", shipped);
        Assert.Contains("\"ExternalAbruptFailureProtectionVerified\": false", shipped);
    }

    // --- Helpers ---

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static bool HasCameraRegistrations(IServiceCollection services)
        => services.Any(s => s.ServiceType == typeof(CameraStatusMonitor))
            || services.Any(
                s => s.ServiceType == typeof(IHostedService)
                    && s.ImplementationType == typeof(CameraMonitorHostedService))
            || services.Any(s => s.ServiceType == typeof(IHttpClientFactory));

    private static string Describe(ServiceDescriptor descriptor)
        => $"{descriptor.ServiceType.FullName}/{descriptor.ImplementationType?.FullName ?? "factory"}/{descriptor.Lifetime}";

    private static CameraStatusMonitor CreateMonitorWithStatus(CameraStatus status)
    {
        var monitor = new CameraStatusMonitor(
            "http://frontend/camera/",
            new HttpClient(new StatusCodeHandler(200)));

        if (status == CameraStatus.Streaming)
        {
            // One headers-only 2xx probe moves the cache to Streaming.
            monitor.ProbeOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return monitor;
    }

    private sealed class StatusCodeHandler(int statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode));
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(
                new HttpRequestException(
                    "No connection could be made because the target machine actively refused it."));
    }

    private sealed class StubController : IDroneController
    {
        public List<string> Calls { get; } = [];

        public DroneStatus NextStatus { get; set; } = new();

        public Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("status");
            return Task.FromResult(NextStatus);
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("connect");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }

        public Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
        {
            Calls.Add($"command:{command}");
            return Task.CompletedTask;
        }

        public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default)
        {
            Calls.Add($"speed:{speed}");
            return Task.CompletedTask;
        }
    }
}
