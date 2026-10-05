using System.Net;
using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Probe-loop tests for the Task 31 camera monitor
/// (specs/hardware/camera-runtime.md): headers-only probes whose response body
/// is never read, transition-only publication, honest failure mapping, and
/// quiet cancellation — all against in-memory stubs (no sockets, no
/// wall-clock cadence sleeps; gates drive the loop deterministically).
/// </summary>
public class CameraStatusMonitorTests
{
    [Fact]
    public void BeforeAnyProbe_CurrentIsConnecting_AndNothingWasCalled()
    {
        var handler = new ScriptedHandler();
        var monitor = CreateMonitor(handler);

        Assert.Equal(CameraStatus.Connecting, monitor.Current);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void ProductionCadence_IsFiveSecondIntervalWithThreeSecondTimeout()
    {
        // Software constants fixed by the spec — not configuration in this
        // task and not camera or electrical facts.
        Assert.Equal(TimeSpan.FromSeconds(5), CameraStatusMonitor.ProbeInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), CameraStatusMonitor.ProbeTimeout);
    }

    [Fact]
    public async Task Success2xx_PublishesStreaming_NeverReadsBody_AndProbesImmediatelyAtStart()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueStatusCode(HttpStatusCode.OK);
        var transitions = new List<(CameraStatus Previous, CameraStatus Current)>();
        var script = new DelayScript(steps: 1);
        var monitor = CreateMonitor(
            handler,
            delay: script.Delay,
            onTransition: (previous, current) => transitions.Add((previous, current)));

        monitor.Start();
        await script.ReachedAsync(0);

        Assert.Equal(CameraStatus.Streaming, monitor.Current);
        Assert.Equal(1, handler.Calls);
        Assert.NotNull(handler.LastBody);
        Assert.Equal(0, handler.LastBody!.BodyReads);
        var transition = Assert.Single(transitions);
        Assert.Equal((CameraStatus.Connecting, CameraStatus.Streaming), transition);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non2xxStatus_PublishesError_WithASingleTransition(HttpStatusCode statusCode)
    {
        var handler = new ScriptedHandler();
        handler.EnqueueStatusCode(statusCode);
        var transitions = new List<(CameraStatus Previous, CameraStatus Current)>();
        var script = new DelayScript(steps: 1);
        var monitor = CreateMonitor(
            handler,
            delay: script.Delay,
            onTransition: (previous, current) => transitions.Add((previous, current)));

        monitor.Start();
        await script.ReachedAsync(0);

        Assert.Equal(CameraStatus.Error, monitor.Current);
        var transition = Assert.Single(transitions);
        Assert.Equal((CameraStatus.Connecting, CameraStatus.Error), transition);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProbeTimeout_PublishesError()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueHangUntilCancelled();
        var script = new DelayScript(steps: 1);
        var monitor = CreateMonitor(handler, delay: script.Delay, probeTimeout: TimeSpan.FromMilliseconds(100));

        monitor.Start();
        await script.ReachedAsync(0);

        Assert.Equal(CameraStatus.Error, monitor.Current);
        Assert.Equal(1, handler.Calls);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectionRefusal_PublishesError_NeverThrowsOutOfTheLoop()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueRefusal();
        var script = new DelayScript(steps: 1);
        var monitor = CreateMonitor(handler, delay: script.Delay);

        monitor.Start();
        await script.ReachedAsync(0);

        Assert.Equal(CameraStatus.Error, monitor.Current);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Recovery_AfterFailure_TransitionsBackToStreamingAutomatically()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueRefusal();
        handler.EnqueueStatusCode(HttpStatusCode.OK);
        var transitions = new List<(CameraStatus Previous, CameraStatus Current)>();
        var script = new DelayScript(steps: 2);
        var monitor = CreateMonitor(
            handler,
            delay: script.Delay,
            onTransition: (previous, current) => transitions.Add((previous, current)));

        monitor.Start();
        await script.ReachedAsync(0);
        Assert.Equal(CameraStatus.Error, monitor.Current);

        script.Release(0);
        await script.ReachedAsync(1);
        Assert.Equal(CameraStatus.Streaming, monitor.Current);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(
            new[]
            {
                (CameraStatus.Connecting, CameraStatus.Error),
                (CameraStatus.Error, CameraStatus.Streaming),
            },
            transitions);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedFailures_NotifyOnce_TransitionsNotAttempts()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueRefusal();
        var transitions = new List<(CameraStatus Previous, CameraStatus Current)>();
        var script = new DelayScript(steps: 3);
        var monitor = CreateMonitor(
            handler,
            delay: script.Delay,
            onTransition: (previous, current) => transitions.Add((previous, current)));

        monitor.Start();
        await script.ReachedAsync(0);
        script.Release(0);
        await script.ReachedAsync(1);
        script.Release(1);
        await script.ReachedAsync(2);

        Assert.Equal(3, handler.Calls);
        var transition = Assert.Single(transitions);
        Assert.Equal((CameraStatus.Connecting, CameraStatus.Error), transition);

        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopDuringAnInFlightProbe_EndsQuietly_WithoutPublishingAnError()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueHangUntilCancelled();
        var transitions = new List<(CameraStatus Previous, CameraStatus Current)>();
        var monitor = CreateMonitor(handler, onTransition: (previous, current) => transitions.Add((previous, current)));

        monitor.Start();
        Assert.Equal(1, handler.Calls);

        // Cancels the hanging probe through the loop token; must complete
        // promptly and never surface as a camera error.
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(CameraStatus.Connecting, monitor.Current);
        Assert.Empty(transitions);
        Assert.Equal(1, handler.Calls);
    }

    private static CameraStatusMonitor CreateMonitor(
        ScriptedHandler handler,
        Func<CancellationToken, Task>? delay = null,
        TimeSpan? probeTimeout = null,
        Action<CameraStatus, CameraStatus>? onTransition = null)
        => new(
            "http://camera.test/camera/",
            new HttpClient(handler),
            onTransition,
            probeTimeout ?? TimeSpan.FromSeconds(1),
            delay ?? (static cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)));

    /// <summary>
    /// Deterministic inter-attempt delay: the loop signals that it reached
    /// step N, then parks until the test releases it (or the loop is stopped).
    /// </summary>
    private sealed class DelayScript
    {
        private readonly TaskCompletionSource[] _reached;
        private readonly TaskCompletionSource[] _release;
        private int _calls;

        public DelayScript(int steps)
        {
            _reached = new TaskCompletionSource[steps];
            _release = new TaskCompletionSource[steps];
            for (var index = 0; index < steps; index++)
            {
                _reached[index] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _release[index] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public Func<CancellationToken, Task> Delay => async cancellationToken =>
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            if (index >= _reached.Length)
            {
                throw new InvalidOperationException(
                    $"The probe loop reached delay #{index + 1} beyond the scripted {_reached.Length} step(s).");
            }

            _reached[index].TrySetResult();
            await _release[index].Task.WaitAsync(cancellationToken);
        };

        public Task ReachedAsync(int index)
            => _reached[index].Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release(int index)
            => _release[index].TrySetResult();
    }

    /// <summary>Queued probe behaviors; the last step repeats when the script is exhausted.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _steps = new();
        private Func<CancellationToken, Task<HttpResponseMessage>>? _next;

        public int Calls { get; private set; }

        public BombContent? LastBody { get; private set; }

        public void EnqueueStatusCode(HttpStatusCode statusCode)
            => _steps.Enqueue(_ =>
            {
                var content = new BombContent();
                LastBody = content;
                return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
            });

        public void EnqueueRefusal()
            => _steps.Enqueue(_ => Task.FromException<HttpResponseMessage>(
                new HttpRequestException(
                    "No connection could be made because the target machine actively refused it.")));

        public void EnqueueHangUntilCancelled()
            => _steps.Enqueue(async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (_steps.Count > 0)
            {
                _next = _steps.Dequeue();
            }

            return _next is null
                ? Task.FromException<HttpResponseMessage>(
                    new InvalidOperationException("ScriptedHandler has no step to run."))
                : _next(cancellationToken);
        }
    }

    /// <summary>
    /// Response body that fails if anything ever reads it — the probe must
    /// consume response headers only (decision D4: no video byte in-process).
    /// </summary>
    private sealed class BombContent : HttpContent
    {
        public int BodyReads { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            BodyReads++;
            throw new InvalidOperationException("The camera probe must never read the response body.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
