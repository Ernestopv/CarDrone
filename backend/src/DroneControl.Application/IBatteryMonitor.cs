using DroneControl.Domain;

namespace DroneControl.Application;

/// <summary>
/// Read-only battery telemetry contract consumed by the API. Implementations
/// return an honest <see cref="BatteryStatus"/>: a failed read yields an
/// unavailable/error status rather than throwing or fabricating a voltage.
/// The contract carries no I2C/device/register vocabulary; those belong to the
/// Infrastructure implementation and its configuration.
/// </summary>
public interface IBatteryMonitor
{
    /// <summary>Returns the current battery reading.</summary>
    Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
