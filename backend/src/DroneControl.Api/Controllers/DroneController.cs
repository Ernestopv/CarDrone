using DroneControl.Api.Models;
using DroneControl.Application;
using DroneControl.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DroneControl.Api.Controllers;

/// <summary>
/// Thin HTTP surface over <see cref="IDroneService"/>: validates request input
/// at the boundary, delegates, and returns the resulting domain status through
/// the API wire mapping (camelCase properties, lowercase enum values). Contains
/// no business logic and no exception handling — error policy for execution
/// failures belongs to the backend error-handling task. A 200 response means
/// the (currently simulated) operation completed downstream; it never claims
/// physical hardware confirmation.
/// </summary>
[ApiController]
[Route("api/drone")]
public sealed class DroneController : ControllerBase
{
    private readonly IDroneService _droneService;

    /// <summary>Creates the controller over the application service.</summary>
    public DroneController(IDroneService droneService)
    {
        _droneService = droneService;
    }

    /// <summary>Returns the current drone status.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<DroneStatus>> GetStatus(CancellationToken cancellationToken)
        => Ok(await _droneService.GetStateAsync(cancellationToken));

    /// <summary>Connects to the drone and returns the resulting status.</summary>
    [HttpPost("connect")]
    public async Task<ActionResult<DroneStatus>> Connect(CancellationToken cancellationToken)
    {
        await _droneService.ConnectAsync(cancellationToken);
        return Ok(await _droneService.GetStateAsync(cancellationToken));
    }

    /// <summary>Disconnects and returns the resulting (initial) status.</summary>
    [HttpPost("disconnect")]
    public async Task<ActionResult<DroneStatus>> Disconnect(CancellationToken cancellationToken)
    {
        await _droneService.DisconnectAsync(cancellationToken);
        return Ok(await _droneService.GetStateAsync(cancellationToken));
    }

    /// <summary>
    /// Sends a semantic command and returns the status after its simulated
    /// acknowledgement.
    /// </summary>
    [HttpPost("command")]
    public async Task<ActionResult<DroneStatus>> SendCommand(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Command is not { } command)
        {
            // A missing command must never be interpreted as a default command.
            ModelState.AddModelError(nameof(CommandRequest.Command), "The command field is required.");
            return ValidationProblem();
        }

        await _droneService.SendCommandAsync(command, cancellationToken);
        return Ok(await _droneService.GetStateAsync(cancellationToken));
    }

    /// <summary>Sets the speed percentage (0-100) and returns the resulting status.</summary>
    [HttpPut("speed")]
    public async Task<ActionResult<DroneStatus>> SetSpeed(SpeedRequest request, CancellationToken cancellationToken)
    {
        if (request.Speed is not { } speed)
        {
            // A missing speed must never be interpreted as 0.
            ModelState.AddModelError(nameof(SpeedRequest.Speed), "The speed field is required.");
            return ValidationProblem();
        }

        if (speed is < 0 or > 100)
        {
            // Rejected at the API boundary, before the service is called.
            ModelState.AddModelError(nameof(SpeedRequest.Speed), "Speed must be between 0 and 100.");
            return ValidationProblem();
        }

        await _droneService.SetSpeedAsync(speed, cancellationToken);
        return Ok(await _droneService.GetStateAsync(cancellationToken));
    }
}
