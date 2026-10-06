using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// Deterministic simulated battery reading for <c>BATTERY_MODE=mock</c> (PC
/// development). Clearly simulated: <see cref="BatteryStatus.Simulated"/> is
/// <c>true</c>; there is no I2C access, no hardware, and no timing. This is the
/// only battery implementation that ever reports <c>Simulated=true</c>.
/// </summary>
public sealed class MockBatteryMonitor : IBatteryMonitor
{
    /// <inheritdoc />
    public Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new BatteryStatus
        {
            Available = true,
            Voltage = 7.8,
            Percent = 64,
            State = BatteryState.Ok,
            Simulated = true,
            Current = -0.45,
            Power = -3.51,
        });
    }
}
