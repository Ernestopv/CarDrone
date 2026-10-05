namespace DroneControl.Api.Models;

/// <summary>
/// Body for <c>PUT /api/drone/speed</c>. The value is nullable so a body that
/// omits the field is rejected with HTTP 400 instead of silently defaulting
/// the speed to 0.
/// </summary>
public sealed record SpeedRequest(int? Speed);
