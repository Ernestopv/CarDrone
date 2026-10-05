using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Probe-backed camera-pipeline monitor
/// (specs/hardware/camera-runtime.md, Task 31). Registered only when
/// <c>CAMERA_MODE=ustreamer</c>. Owns the probe loop: an immediate first
/// attempt at host start, then one attempt every <see cref="ProbeInterval"/>,
/// each bounded by <see cref="ProbeTimeout"/>.
/// <para>
/// The probe is a headers-only GET (<see cref="HttpCompletionOption.ResponseHeadersRead"/>):
/// success is an HTTP 2xx within the timeout, the response body is never read,
/// and no video byte ever enters the process (decision D4 of the runtime
/// design — the backend never proxies video).
/// </para>
/// <para>
/// The cached <see cref="Current"/> value is what
/// <see cref="CameraStatusOverlay"/> reports, so status reads never perform
/// network I/O, and only state transitions are surfaced through the callback
/// (never one log line per failed attempt). <c>Streaming</c> means "the
/// configured stream endpoint answered 2xx" — endpoint reachability, never
/// verified picture or content.
/// </para>
/// </summary>
public sealed class CameraStatusMonitor
{
    /// <summary>
    /// Delay between probe attempts after the immediate first one. A software
    /// cadence, not an electrical or camera fact.
    /// </summary>
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Per-attempt deadline. A software timeout, not an electrical or camera
    /// fact.
    /// </summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly string _probeUrl;
    private readonly HttpClient _httpClient;
    private readonly Action<CameraStatus, CameraStatus>? _onTransition;
    private readonly TimeSpan _probeTimeout;
    private readonly Func<CancellationToken, Task> _delay;
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Starts at <see cref="CameraStatus.Connecting"/> until the first attempt completes.</summary>
    private volatile CameraStatus _current = CameraStatus.Connecting;

    /// <summary>Production constructor: fixed 3-second timeout, 5-second cadence.</summary>
    public CameraStatusMonitor(
        string probeUrl,
        HttpClient httpClient,
        Action<CameraStatus, CameraStatus>? onTransition = null)
        : this(
            probeUrl,
            httpClient,
            onTransition,
            ProbeTimeout,
            static token => Task.Delay(ProbeInterval, token))
    {
    }

    /// <summary>
    /// Test seam (InternalsVisibleTo): injectable timeout and inter-attempt
    /// delay so tests run deterministically without wall-clock cadence sleeps.
    /// </summary>
    internal CameraStatusMonitor(
        string probeUrl,
        HttpClient httpClient,
        Action<CameraStatus, CameraStatus>? onTransition,
        TimeSpan probeTimeout,
        Func<CancellationToken, Task> delay)
    {
        _probeUrl = string.IsNullOrWhiteSpace(probeUrl)
            ? throw new ArgumentException("A camera probe URL is required.", nameof(probeUrl))
            : probeUrl.Trim();
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _onTransition = onTransition;
        _probeTimeout = probeTimeout > TimeSpan.Zero
            ? probeTimeout
            : throw new ArgumentOutOfRangeException(
                nameof(probeTimeout), probeTimeout, "The camera probe timeout must be positive.");
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    /// <summary>
    /// Latest observed pipeline state, updated atomically after each attempt.
    /// Reading it never triggers network activity.
    /// </summary>
    public CameraStatus Current => _current;

    /// <summary>
    /// Starts the probe loop. Invoked once by the hosted lifecycle adapter
    /// (<c>CameraMonitorHostedService</c>); repeated calls are ignored.
    /// </summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = RunAsync(_cts.Token);
        }
    }

    /// <summary>
    /// Cancels the loop and waits for it to end. Host-shutdown cancellation is
    /// honored quietly — never surfaced as a camera error.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_lifecycleGate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }

        if (loop is null || cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host shutdown deadline expired while the loop was ending;
            // the loop observes its own token. Not an error.
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Performs exactly one probe and publishes the outcome. Internal test
    /// seam — production traffic only flows through the loop started by
    /// <see cref="Start"/>.
    /// </summary>
    /// <returns><c>true</c> when the attempt succeeded (2xx within timeout).</returns>
    internal async Task<bool> ProbeOnceAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_probeTimeout);

        CameraStatus next;
        try
        {
            // Headers only: the response is disposed without ever touching the
            // body, so no video byte is buffered, inspected, or relayed.
            using var response = await _httpClient
                .GetAsync(_probeUrl, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            next = response.IsSuccessStatusCode
                ? CameraStatus.Streaming
                : CameraStatus.Error;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: let the loop end quietly.
            throw;
        }
        catch (Exception)
        {
            // Timeout, refused connection, DNS failure — every probe failure
            // maps to the same honest Error. It never propagates: a camera
            // problem must not reach status reads or the control plane (D4).
            next = CameraStatus.Error;
        }

        Publish(next);
        return next == CameraStatus.Streaming;
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // First attempt runs immediately at host start; only after it
                // completes does the cadence delay elapse.
                await ProbeOnceAsync(token).ConfigureAwait(false);
                await _delay(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Shutdown path: end without publishing or logging an error.
                return;
            }
        }
    }

    private void Publish(CameraStatus next)
    {
        var previous = _current;
        if (previous == next)
        {
            // Transition-only publication: repeated failures of the same state
            // never repeat-notify (no per-attempt log spam).
            return;
        }

        _current = next;
        _onTransition?.Invoke(previous, next);
    }
}
