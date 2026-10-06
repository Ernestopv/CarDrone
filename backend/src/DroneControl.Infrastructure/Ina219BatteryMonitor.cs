using DroneControl.Application;
using DroneControl.Domain;

namespace DroneControl.Infrastructure;

/// <summary>
/// INA219 battery monitor: reads the sensor's bus- and shunt-voltage registers
/// over the injected <see cref="II2cBus"/> and converts them to volts using the
/// device's documented LSBs. Current and power are computed in software
/// (<c>current = shuntVolts / ShuntOhms</c>, <c>power = busVolts × current</c>).
/// The percent is a linear voltage-window approximation, not a fuel gauge.
/// <para>
/// The monitor is deliberately <b>read-only</b>: it never writes the
/// configuration or calibration registers. A failed read (bus or shunt) is
/// reported as an honest unavailable/error status — never a partial or
/// fabricated reading and never a thrown request failure that would take down
/// the control plane. Failures are surfaced through the injected callback (the
/// composition root logs them), mirroring the other Infrastructure types that
/// avoid a logging dependency.
/// </para>
/// </summary>
public sealed class Ina219BatteryMonitor : IBatteryMonitor
{
    // INA219 register map (TI): bus-voltage register 0x02 (bits 15..3 in 4 mV
    // units; bit 1 CNVR, bit 0 OVF) and shunt-voltage register 0x01 (signed
    // 16-bit, 10 µV LSB). Current/power are computed in software from the raw
    // shunt voltage and the operator shunt resistance — the calibration
    // register is deliberately never written (the sensor stays read-only).
    internal const byte BusVoltageRegister = 0x02;
    internal const byte ShuntVoltageRegister = 0x01;
    internal const double BusVoltageLsbVolts = 0.004;
    internal const double ShuntVoltageLsbVolts = 0.00001;

    private readonly II2cBus _bus;
    private readonly BatteryOptions _options;
    private readonly Action<Exception>? _onReadFailure;

    /// <summary>Creates the monitor over its bus and validated configuration.</summary>
    public Ina219BatteryMonitor(II2cBus bus, BatteryOptions options, Action<Exception>? onReadFailure = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _onReadFailure = onReadFailure;
    }

    /// <inheritdoc />
    public Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var busRaw = _bus.ReadWord(_options.I2cAddress, BusVoltageRegister);
            var shuntRaw = _bus.ReadWord(_options.I2cAddress, ShuntVoltageRegister);

            var voltage = (busRaw >> 3) * BusVoltageLsbVolts;
            var shuntVolts = (short)shuntRaw * ShuntVoltageLsbVolts;
            var current = shuntVolts / _options.ShuntOhms;
            var power = voltage * current;
            var percent = ComputePercent(voltage);

            return Task.FromResult(new BatteryStatus
            {
                Available = true,
                Voltage = voltage,
                Percent = percent,
                State = Classify(percent),
                Simulated = false,
                Current = current,
                Power = power,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _onReadFailure?.Invoke(exception);

            return Task.FromResult(new BatteryStatus
            {
                Available = false,
                Voltage = null,
                Percent = null,
                State = BatteryState.Error,
                Simulated = false,
            });
        }
    }

    private int ComputePercent(double voltage)
    {
        var ratio = (voltage - _options.EmptyVoltage) / (_options.FullVoltage - _options.EmptyVoltage);
        var percent = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }

    private BatteryState Classify(int percent)
    {
        if (percent <= _options.CriticalPercent)
        {
            return BatteryState.Critical;
        }

        if (percent <= _options.LowPercent)
        {
            return BatteryState.Low;
        }

        return BatteryState.Ok;
    }
}
