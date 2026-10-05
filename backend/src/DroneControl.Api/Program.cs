using System.Text.Json;
using System.Text.Json.Serialization;
using DroneControl.Api;
using DroneControl.Api.Errors;
using DroneControl.Application;
using DroneControl.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Standard ASP.NET Core foundation: the DI container, logging and
// configuration all come from the host builder — no external frameworks.
builder.Services.AddOpenApi();

// Task 23: hardware runtime selection happens HERE, in the composition root
// (specs/hardware/raspberry-hardware-provider.md). Unset or 'mock' keeps the
// exact Task 11 graph (the simulator is a singleton because it owns session
// state across requests and scoped DroneService instances); 'real' registers
// the Raspberry provider behind the hardware-backed controller atomically.
var hardwareMode = builder.Services.AddDroneRuntime(builder.Configuration);

// Task 31: camera runtime selection happens HERE as well, beside hardware
// mode (specs/hardware/camera-runtime.md). Unset or 'mock' keeps the exact
// simulated camera behavior (nothing camera-related is registered and the
// Camera: section is never read); 'ustreamer' validates the Raspberry
// platform gate plus a required Camera:ProbeUrl, then registers the
// probe-backed monitor that feeds DroneStatus.camera.
var cameraMode = builder.Services.AddCameraRuntime(builder.Configuration);

// Task 26: safety limits are explicit configuration, validated eagerly before
// the host can accept requests. These are software deadlines, not electrical
// or motor limits (specs/hardware/failsafe.md).
var safetyOptions = new DroneSafetyOptions(
    builder.Configuration.GetValue<int?>("Safety:CommandTimeoutMilliseconds")
        ?? throw new InvalidOperationException("Safety:CommandTimeoutMilliseconds is required."),
    builder.Configuration.GetValue<int?>("Safety:StopTimeoutMilliseconds")
        ?? throw new InvalidOperationException("Safety:StopTimeoutMilliseconds is required."),
    builder.Configuration.GetValue<int?>("Safety:StopRetryCount")
        ?? throw new InvalidOperationException("Safety:StopRetryCount is required."),
    builder.Configuration.GetValue<int?>("Safety:StopRetryDelayMilliseconds")
        ?? throw new InvalidOperationException("Safety:StopRetryDelayMilliseconds is required."),
    builder.Configuration.GetValue<int?>("Safety:RecoveryRetryCount")
        ?? throw new InvalidOperationException("Safety:RecoveryRetryCount is required."),
    builder.Configuration.GetValue<int?>("Safety:RecoveryRetryDelayMilliseconds")
        ?? throw new InvalidOperationException("Safety:RecoveryRetryDelayMilliseconds is required."),
    realHardwareMode: hardwareMode == HardwareMode.Real,
    externalAbruptFailureProtectionVerified:
        builder.Configuration.GetValue<bool>("Safety:ExternalAbruptFailureProtectionVerified"));

builder.Services.AddSingleton(safetyOptions);
builder.Services.AddSingleton<DroneSafetyController>(services =>
{
    IDroneController inner = hardwareMode switch
    {
        HardwareMode.Mock => services.GetRequiredService<MockDroneController>(),
        HardwareMode.DryRun => services.GetRequiredService<HardwareDroneController>(),
        HardwareMode.Real => services.GetRequiredService<HardwareDroneController>(),
        _ => throw new InvalidOperationException($"Unexpected resolved hardware mode '{hardwareMode}'."),
    };

    // Task 31: in ustreamer mode the camera overlay remaps ONLY State.Camera
    // from the probe cache, composed BELOW the safety decorator so the
    // safety-closed Offline override still wins (camera-runtime.md). In mock
    // mode this returns the inner controller untouched — the overlay does not
    // exist in the graph.
    inner = CameraRuntimeSelection.ApplyCameraOverlay(inner, cameraMode, services);

    var logger = services.GetRequiredService<ILogger<DroneSafetyController>>();
    return new DroneSafetyController(inner, safetyOptions, safetyEvent =>
    {
        if (safetyEvent.State is DroneSafetyState.Faulted or DroneSafetyState.StopPending)
        {
            logger.LogWarning("Drone safety {SafetyReason} in {SafetyState}: {SafetyMessage}; exception={ExceptionType}",
                safetyEvent.Reason, safetyEvent.State, safetyEvent.Message, safetyEvent.ExceptionType);
        }
        else
        {
            logger.LogInformation("Drone safety {SafetyReason} in {SafetyState}: {SafetyMessage}",
                safetyEvent.Reason, safetyEvent.State, safetyEvent.Message);
        }
    });
});
builder.Services.AddSingleton<IDroneController>(services => services.GetRequiredService<DroneSafetyController>());
builder.Services.AddHostedService<SafetyLifecycleHostedService>();
builder.Services.AddScoped<IDroneService, DroneService>();

// Task 12: controller endpoints under /api/drone. Enum values follow the
// domain-model wire table: lowercase strings only (numeric enum values are not
// part of the contract); property names stay on the ASP.NET Core camelCase
// default. Health keeps its minimal-API route.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});

// Task 16: consistent error contract (specs/backend/error-handling.md). The
// problem pipeline answers every environment with ProblemDetails JSON — the
// exception handler maps the known Application failures (409/503) and the
// default pipeline turns anything else into a logged, generic 500. No stack
// trace or developer page ever reaches a client; developers get the detail
// in the logs instead.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DroneExceptionHandler>();

// Development CORS: allowed React origins come from configuration so no
// origin is hardcoded (see appsettings.Development.json).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Honest posture on supported hardware: the concrete Linux sinks exist. An
// asserted MotorMapping routes commands to the real GPIO character-device sink
// and an asserted PwmMapping routes speed to the real PWM sysfs sink. Without
// operator assertions the provider stays inert (honest unavailable) — never a
// silent simulation under 'real'.
if (hardwareMode == HardwareMode.Real)
{
    app.Logger.LogWarning(
        "HARDWARE_MODE=real is active: asserted mappings drive the real GPIO/PWM sinks; " +
        "without an asserted mapping the provider reports unavailable (no silent simulation).");
}
else if (hardwareMode == HardwareMode.DryRun)
{
    // Task 27/29/30 posture: the dry-run graph contains the suppressing
    // output sinks only — there is no real GPIO/PWM sink to reach. Without
    // operator assertions the provider stays inert (honest unavailable); a
    // direction assertion routes commands to the suppressing sink, a speed
    // assertion routes speed to the suppressing PWM sink, independently. An
    // assertion is never physical verification
    // (specs/hardware/motor-control.md, specs/hardware/pwm.md).
    var directionAsserted = builder.Configuration.GetValue<bool>("MotorMapping:DirectionMappingVerified");
    var speedAsserted = builder.Configuration.GetValue<bool>("PwmMapping:SpeedMappingVerified");
    if (directionAsserted && speedAsserted)
    {
        app.Logger.LogWarning(
            "HARDWARE_MODE=dry-run is active with operator-asserted motor and PWM mappings: intended " +
            "GPIO and PWM output operations are computed from the asserted mappings and recorded and " +
            "suppressed at the dry-run output boundary (no physical output sink exists in this graph). " +
            "Both mappings are operator assertions, NOT physical verification; the PWM envelope has " +
            "not been bench-verified.");
    }
    else if (directionAsserted)
    {
        app.Logger.LogWarning(
            "HARDWARE_MODE=dry-run is active with an operator-asserted motor mapping: intended GPIO " +
            "output operations are computed from the asserted mapping and recorded and suppressed at " +
            "the dry-run output boundary (no physical output sink exists in this graph). The mapping " +
            "is an operator assertion, NOT physical verification; speed is not configured (no " +
            "asserted PwmMapping).");
    }
    else if (speedAsserted)
    {
        app.Logger.LogWarning(
            "HARDWARE_MODE=dry-run is active with an operator-asserted PWM mapping: intended PWM " +
            "output operations are computed from the asserted envelope and recorded and suppressed " +
            "at the dry-run output boundary (no physical output sink exists in this graph). The " +
            "envelope is an operator assertion, NOT physical verification; direction is not " +
            "configured (no asserted MotorMapping).");
    }
    else
    {
        app.Logger.LogWarning(
            "HARDWARE_MODE=dry-run is active: intended GPIO/PWM operations are recorded " +
            "and suppressed at the dry-run output boundary (no physical output sink exists " +
            "in this graph), and no operator-asserted mappings are configured — " +
            "hardware operations report unavailable until mappings are asserted after bench verification.");
    }
}

// Task 31 camera posture (specs/hardware/camera-runtime.md): mode selection
// only — 'streaming' ever means the stream endpoint answered a headers-only
// probe (endpoint reachability), never verified video. In mock mode no probe
// exists: camera status keeps today's semantics (simulator-derived, or honest
// offline in hardware graphs).
if (cameraMode == CameraMode.Ustreamer)
{
    app.Logger.LogInformation(
        "CAMERA_MODE=ustreamer: camera status reports stream-endpoint reachability " +
        "from a headers-only probe (never verified video).");
}
else
{
    app.Logger.LogInformation(
        "CAMERA_MODE=mock: no camera probe is registered; camera status keeps " +
        "today's semantics (simulator-derived or honest offline).");
}

// Task 16: catch failures before anything else touches the response. The
// host auto-inserts the developer exception page outermost, so this handler
// runs first for endpoint exceptions in every environment — ProblemDetails
// JSON everywhere, stacks only in the logs.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("DevelopmentFrontend");
}

app.UseHttpsRedirection();

// Liveness endpoint for the backend foundation. Drone endpoints arrive with
// their own specifications (backend Tasks 10-12), not with this task.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

// Task 12: drone REST endpoints (specs/backend/drone-api.md) are mapped
// alongside the minimal-API health route above.
app.MapControllers();

app.Run();

// Test seam for WebApplicationFactory<Program> in DroneControl.Api.Tests
// (specs/backend/backend-tests.md). Zero runtime behavior change.
public partial class Program
{
}
