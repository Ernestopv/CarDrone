using DroneControl.Domain;

namespace DroneControl.Api.Models;

/// <summary>
/// Body for <c>POST /api/drone/command</c>. The value is nullable so a body
/// that omits the field is rejected with HTTP 400 instead of binding a default
/// command.
/// </summary>
public sealed record CommandRequest(DroneCommand? Command);
