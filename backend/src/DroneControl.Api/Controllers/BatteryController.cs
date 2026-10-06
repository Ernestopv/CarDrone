using DroneControl.Application;
using DroneControl.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DroneControl.Api.Controllers;

/// <summary>
/// Thin HTTP surface over <see cref="IBatteryMonitor"/>: returns the current
/// battery reading as-is. Contains no I2C logic and no exception handling — a
/// read failure is already an honest unavailable/error status object, so the
/// endpoint never returns a 5xx for an unreachable sensor and never validates
/// or transforms the measurement. This endpoint is additive; the frozen
/// <c>DroneStatus</c> wire contract is untouched.
/// </summary>
[ApiController]
[Route("api/battery")]
public sealed class BatteryController : ControllerBase
{
    private readonly IBatteryMonitor _batteryMonitor;

    /// <summary>Creates the controller over the battery monitor.</summary>
    public BatteryController(IBatteryMonitor batteryMonitor)
    {
        _batteryMonitor = batteryMonitor;
    }

    /// <summary>Returns the current battery reading.</summary>
    [HttpGet]
    public async Task<ActionResult<BatteryStatus>> GetStatus(CancellationToken cancellationToken)
        => Ok(await _batteryMonitor.GetStatusAsync(cancellationToken));
}
