using DroneControl.Domain;
using DroneControl.Infrastructure;

namespace DroneControl.Application.Tests;

/// <summary>
/// Battery monitoring implementation tests
/// (specs/hardware/battery-monitoring.md): the validated <see cref="BatteryOptions"/>
/// binding, the read-only <see cref="Ina219BatteryMonitor"/> over a fake I2C
/// bus, and the deterministic mock monitor.
/// </summary>
public class BatteryMonitorTests
{
    // --- BatteryOptions binding ---

    [Fact]
    public void BatteryOptions_ValidSection_BindsWithDocumentedDefaults()
    {
        var options = Options(
            ("I2cAddress", "64"),
            ("FullVoltage", "8.4"),
            ("EmptyVoltage", "6.0"),
            ("ShuntOhms", "0.1"));

        Assert.Equal(BatteryOptions.DefaultI2cBusPath, options.I2cBusPath);
        Assert.Equal(64, options.I2cAddress);
        Assert.Equal(8.4, options.FullVoltage);
        Assert.Equal(6.0, options.EmptyVoltage);
        Assert.Equal(0.1, options.ShuntOhms);
        Assert.Equal(BatteryOptions.DefaultLowPercent, options.LowPercent);
        Assert.Equal(BatteryOptions.DefaultCriticalPercent, options.CriticalPercent);
    }

    [Fact]
    public void BatteryOptions_HexAndDecimalAddresses_AreAccepted()
    {
        var hex = BatteryOptions.FromSection(new Dictionary<string, string?>
        {
            ["I2cAddress"] = "0x40",
            ["FullVoltage"] = "8.4",
            ["EmptyVoltage"] = "6.0",
            ["ShuntOhms"] = "0.1",
        });
        var dec = BatteryOptions.FromSection(new Dictionary<string, string?>
        {
            ["I2cAddress"] = "64",
            ["FullVoltage"] = "8.4",
            ["EmptyVoltage"] = "6.0",
            ["ShuntOhms"] = "0.1",
        });

        Assert.Equal(0x40, hex.I2cAddress);
        Assert.Equal(64, dec.I2cAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("not-a-number")]
    public void BatteryOptions_InvalidShuntOhms_IsRejectedNamingTheKey(string? shunt)
    {
        var values = new Dictionary<string, string?>
        {
            ["I2cAddress"] = "64",
            ["FullVoltage"] = "8.4",
            ["EmptyVoltage"] = "6.0",
        };
        if (shunt is not null)
        {
            values["ShuntOhms"] = shunt;
        }

        var ex = Assert.Throws<ArgumentException>(() => BatteryOptions.FromSection(values));

        Assert.Contains("ShuntOhms", ex.Message);
    }

    [Theory]
    [InlineData("0x02")]
    [InlineData("0x78")]
    [InlineData("not-an-address")]
    public void BatteryOptions_InvalidAddress_ReportsEveryViolation(string address)
    {
        var ex = Assert.Throws<ArgumentException>(() => Options(("I2cAddress", address)));

        Assert.Contains("I2cAddress", ex.Message);
    }

    [Fact]
    public void BatteryOptions_MissingI2cAddress_BecomesAFullErrorList()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options());

        Assert.Contains("I2cAddress", ex.Message);
        Assert.Contains("FullVoltage", ex.Message);
        Assert.Contains("EmptyVoltage", ex.Message);
    }

    [Fact]
    public void BatteryOptions_EmptyVoltageNotBelowFull_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options(
            ("FullVoltage", "6.0"),
            ("EmptyVoltage", "8.4")));

        Assert.Contains("EmptyVoltage", ex.Message);
    }

    [Fact]
    public void BatteryOptions_NonPositiveFullVoltage_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options(
            ("FullVoltage", "0"),
            ("EmptyVoltage", "0")));

        Assert.Contains("FullVoltage", ex.Message);
    }

    [Fact]
    public void BatteryOptions_CriticalAboveLow_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options(
            ("LowPercent", "10"),
            ("CriticalPercent", "20")));

        Assert.Contains("CriticalPercent", ex.Message);
    }

    [Fact]
    public void BatteryOptions_AggregatesEveryViolatedRuleIntoOneMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options(
            ("I2cAddress", "0x78"),
            ("FullVoltage", "junk"),
            ("EmptyVoltage", "8.4"),
            ("CriticalPercent", "150")));

        Assert.Contains("I2cAddress", ex.Message);
        Assert.Contains("FullVoltage", ex.Message);
        Assert.Contains("CriticalPercent", ex.Message);
    }

    // --- Ina219BatteryMonitor over a fake bus ---

    [Fact]
    public async Task ReadsBusAndShuntRegisters_ComputesVoltageCurrentAndPower()
    {
        // Bus 0x341A => 6.668 V. Shunt 0xE2DF => signed -7457 => -74.57 mV;
        // at R 0.1 Ω that is -0.7457 A; power = 6.668 × -0.7457 ≈ -4.97 W.
        var bus = new FakeI2cBus((_, register) => (ushort)(register == 0x02 ? 0x341A : 0xE2DF));
        var monitor = new Ina219BatteryMonitor(bus, ValidOptions());

        var status = await monitor.GetStatusAsync();

        Assert.True(status.Available);
        Assert.Equal(6.668, status.Voltage);
        Assert.Equal(28, status.Percent); // round((6.668-6)/2.4×100)
        Assert.Equal(BatteryState.Ok, status.State);
        Assert.False(status.Simulated);
        Assert.Equal(-0.7457, status.Current!.Value, precision: 4);
        Assert.Equal(6.668 * -0.7457, status.Power!.Value, precision: 4);
        Assert.Equal(2, bus.Reads.Count);
        Assert.Equal((0x40, (byte)0x02), bus.Reads[0]);
        Assert.Equal((0x40, (byte)0x01), bus.Reads[1]);
    }

    [Fact]
    public async Task ZeroShuntVoltage_ReportsZeroCurrentAndPower()
    {
        var bus = new FakeI2cBus((_, register) => (ushort)(register == 0x02 ? 0x341A : 0x0000));
        var monitor = new Ina219BatteryMonitor(bus, ValidOptions());

        var status = await monitor.GetStatusAsync();

        Assert.Equal(0.0, status.Current);
        Assert.Equal(0.0, status.Power);
    }

    [Fact]
    public async Task PositiveShuntVoltage_ReportsPositiveCurrent()
    {
        // +7457 × 10 µV = +74.57 mV → +0.7457 A at R 0.1 Ω.
        var bus = new FakeI2cBus((_, register) => (ushort)(register == 0x02 ? 0x341A : 0x1D21));
        var monitor = new Ina219BatteryMonitor(bus, ValidOptions());

        var status = await monitor.GetStatusAsync();

        Assert.Equal(0.7457, status.Current!.Value, precision: 4);
        Assert.Equal(6.668 * 0.7457, status.Power!.Value, precision: 4);
    }

    [Fact]
    public async Task AtFullVoltage_Reports100AndOk()
    {
        var monitor = new Ina219BatteryMonitor(new FakeI2cBus(0x41A0), ValidOptions()); // 8.40 V

        var status = await monitor.GetStatusAsync();

        Assert.True(status.Available);
        Assert.Equal(100, status.Percent);
        Assert.Equal(BatteryState.Ok, status.State);
    }

    [Fact]
    public async Task AtEmptyVoltage_Reports0AndCritical()
    {
        var monitor = new Ina219BatteryMonitor(new FakeI2cBus(0x2EE0), ValidOptions()); // 6.00 V

        var status = await monitor.GetStatusAsync();

        Assert.Equal(0, status.Percent);
        Assert.Equal(BatteryState.Critical, status.State);
    }

    [Fact]
    public async Task LowBoundary_ClassifiesLowAndCriticalExactly()
    {
        // 20 % => 6.48 V => raw 12960 (0x32A0); 10 % => 6.24 V => raw 12480 (0x30C0).
        var low = await new Ina219BatteryMonitor(new FakeI2cBus(0x32A0), ValidOptions()).GetStatusAsync();
        var critical = await new Ina219BatteryMonitor(new FakeI2cBus(0x30C0), ValidOptions()).GetStatusAsync();

        Assert.Equal(20, low.Percent);
        Assert.Equal(BatteryState.Low, low.State);
        Assert.Equal(10, critical.Percent);
        Assert.Equal(BatteryState.Critical, critical.State);
    }

    [Fact]
    public async Task ReadFailure_ReportsUnavailableError_AndInvokesTheCallback()
    {
        var callbackInvoked = false;
        var monitor = new Ina219BatteryMonitor(
            new FakeI2cBus((_, _) => throw new InvalidOperationException("read failed")),
            ValidOptions(),
            exception =>
            {
                callbackInvoked = true;
                Assert.IsType<InvalidOperationException>(exception);
            });

        var status = await monitor.GetStatusAsync();

        Assert.False(status.Available);
        Assert.Null(status.Voltage);
        Assert.Null(status.Percent);
        Assert.Equal(BatteryState.Error, status.State);
        Assert.False(status.Simulated);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsWithoutTouchingTheBus()
    {
        var bus = new FakeI2cBus(0x3CF0);
        var monitor = new Ina219BatteryMonitor(bus, ValidOptions());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => monitor.GetStatusAsync(cts.Token));

        Assert.Empty(bus.Reads);
    }

    // --- MockBatteryMonitor ---

    [Fact]
    public async Task MockMonitor_IsDeterministicAndExplicitlySimulated()
    {
        var monitor = new MockBatteryMonitor();

        var a = await monitor.GetStatusAsync();
        var b = await monitor.GetStatusAsync();

        Assert.True(a.Available);
        Assert.Equal(7.8, a.Voltage);
        Assert.Equal(64, a.Percent);
        Assert.Equal(BatteryState.Ok, a.State);
        Assert.True(a.Simulated);
        Assert.Equal(-0.45, a.Current);
        Assert.Equal(7.8 * -0.45, a.Power);
        Assert.Equal(a, b);
    }

    // --- Domain invariant ---

    [Fact]
    public void BatteryStatus_DefaultConstruction_IsTheHonestNoReadingStatus()
    {
        var status = new BatteryStatus();

        Assert.False(status.Available);
        Assert.Null(status.Voltage);
        Assert.Null(status.Percent);
        Assert.Equal(BatteryState.Unknown, status.State);
        Assert.False(status.Simulated);
        Assert.Null(status.Current);
        Assert.Null(status.Power);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void BatteryStatus_PercentOutOfRange_IsRejectedNeverClamped(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryStatus { Percent = percent });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BatteryStatus { Available = true, Voltage = 8.4, State = BatteryState.Ok } with { Percent = percent });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void BatteryStatus_PercentBoundaries_AreAccepted(int percent)
    {
        var status = new BatteryStatus { Percent = percent };

        Assert.Equal(percent, status.Percent);
    }

    // --- Helpers ---

    private static BatteryOptions ValidOptions()
        => BatteryOptions.FromSection(new Dictionary<string, string?>
        {
            ["I2cBusPath"] = "/dev/i2c-1",
            ["I2cAddress"] = "0x40",
            ["FullVoltage"] = "8.4",
            ["EmptyVoltage"] = "6.0",
            ["ShuntOhms"] = "0.1",
            ["LowPercent"] = "20",
            ["CriticalPercent"] = "10",
        });

    private static BatteryOptions Options(params (string Key, string Value)[] values)
        => BatteryOptions.FromSection(values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase));

    private sealed class FakeI2cBus : II2cBus
    {
        private readonly Func<int, byte, ushort> _read;
        private bool _disposed;

        public FakeI2cBus(ushort value)
            => _read = (_, _) => value;

        public FakeI2cBus(Func<int, byte, ushort> read)
            => _read = read;

        public List<(int Address, byte Register)> Reads { get; } = [];

        public bool Disposed => _disposed;

        public ushort ReadWord(int deviceAddress, byte register)
        {
            Reads.Add((deviceAddress, register));
            return _read(deviceAddress, register);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}