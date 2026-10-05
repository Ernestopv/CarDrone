using System.Net;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Decorator tests for the Task 31 camera overlay
/// (specs/hardware/camera-runtime.md): only State.Camera is remapped from the
/// monitor cache, every operation and every other status field delegates
/// unchanged, and status reads never perform network activity.
/// </summary>
public class CameraStatusOverlayTests
{
    [Fact]
    public async Task GetStatus_ReplacesOnlyTheCameraField()
    {
        var inner = new FakeDroneController
        {
            NextStatus = new DroneStatus
            {
                State = new DroneState
                {
                    Connection = ConnectionStatus.Connected,
                    Camera = CameraStatus.Offline,
                    RequestedCommand = DroneCommand.Left,
                    ConfirmedCommand = DroneCommand.Right,
                    Speed = 42,
                },
                RaspberryPi = ConnectionStatus.Connected,
                Api = ConnectionStatus.Error,
            },
        };
        var overlay = new CameraStatusOverlay(inner, CreateMonitor(new StaticOkHandler()));

        var status = await overlay.GetStatusAsync();

        // Camera comes from the monitor cache (Connecting before any probe),
        // never from the inner controller's value.
        Assert.Equal(CameraStatus.Connecting, status.State.Camera);
        Assert.Equal(ConnectionStatus.Connected, status.State.Connection);
        Assert.Equal(DroneCommand.Left, status.State.RequestedCommand);
        Assert.Equal(DroneCommand.Right, status.State.ConfirmedCommand);
        Assert.Equal(42, status.State.Speed);
        Assert.Equal(ConnectionStatus.Connected, status.RaspberryPi);
        Assert.Equal(ConnectionStatus.Error, status.Api);
    }

    [Fact]
    public async Task GetStatus_ReportsTheObservedPipelineHealth_NotTheInnerCamera()
    {
        var handler = new StaticOkHandler();
        var monitor = CreateMonitor(handler);
        await monitor.ProbeOnceAsync(CancellationToken.None);
        Assert.Equal(CameraStatus.Streaming, monitor.Current);

        var inner = new FakeDroneController
        {
            NextStatus = new DroneStatus
            {
                State = new DroneState { Camera = CameraStatus.Offline },
            },
        };
        var overlay = new CameraStatusOverlay(inner, monitor);

        var status = await overlay.GetStatusAsync();

        Assert.Equal(CameraStatus.Streaming, status.State.Camera);
        Assert.Equal(ConnectionStatus.Offline, status.State.Connection);
    }

    [Fact]
    public async Task EveryOperationDelegatesToTheInnerControllerUnchanged()
    {
        var inner = new FakeDroneController();
        var overlay = new CameraStatusOverlay(inner, CreateMonitor(new StaticOkHandler()));

        await overlay.ConnectAsync();
        await overlay.DisconnectAsync();
        await overlay.SendCommandAsync(DroneCommand.Forward);
        await overlay.SetSpeedAsync(55);
        await overlay.GetStatusAsync();

        Assert.Equal(
            new[]
            {
                "ConnectAsync",
                "DisconnectAsync",
                "SendCommandAsync:Forward",
                "SetSpeedAsync:55",
                "GetStatusAsync",
            },
            inner.Calls);
    }

    [Fact]
    public async Task StatusReads_NeverPerformNetworkActivity()
    {
        var handler = new StaticOkHandler();
        var overlay = new CameraStatusOverlay(
            new FakeDroneController(),
            CreateMonitor(handler));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var status = await overlay.GetStatusAsync();
            Assert.Equal(CameraStatus.Connecting, status.State.Camera);
        }

        // The monitor loop is never started by a status read; reads hit the
        // cache only.
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task WhileAProbeNeverCompletes_StatusReadsStillReturnTheCachedValue()
    {
        var handler = new HangingHandler();
        var monitor = CreateMonitor(handler);
        monitor.Start();

        // The probe is in flight and can never finish. A status read must not
        // wait for it, must not throw, and must not trigger another attempt —
        // if it did, this test would hang instead of returning.
        var overlay = new CameraStatusOverlay(new FakeDroneController(), monitor);
        var status = await overlay.GetStatusAsync();

        Assert.Equal(CameraStatus.Connecting, status.State.Camera);
        Assert.Equal(1, handler.Calls);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InnerFailuresPropagateUnchanged()
    {
        var inner = new FakeDroneController
        {
            ThrowOnSend = new InvalidOperationException("boom"),
        };
        var overlay = new CameraStatusOverlay(inner, CreateMonitor(new StaticOkHandler()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => overlay.SendCommandAsync(DroneCommand.Left));
    }

    private static CameraStatusMonitor CreateMonitor(HttpMessageHandler handler)
        => new("http://camera.test/camera/", new HttpClient(handler));

    private sealed class StaticOkHandler(int statusCode = 200) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode));
        }
    }

    /// <summary>Probe attempt that only ends when its token is cancelled.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
