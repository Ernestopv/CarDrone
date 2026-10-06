namespace DroneControl.Domain;

/// <summary>
/// Immutable battery reading reported to upper layers. Default construction
/// yields the honest "no reading" status: unavailable, no voltage, no percent,
/// <see cref="BatteryState.Unknown"/>, not simulated.
/// <para>
/// <see cref="Voltage"/> is a measured bus/pack voltage. <see cref="Percent"/>
/// is a documented linear voltage approximation, never a fuel-gauge or
/// coulomb-counting claim. This type carries no hardware vocabulary (no bus
/// path, address, register or sensor name) by design.
/// </para>
/// </summary>
public sealed record BatteryStatus
{
    private int? _percent;

    /// <summary><c>false</c> when no valid reading exists (the honest default).</summary>
    public bool Available { get; init; }

    /// <summary>Measured pack/bus voltage in volts; <c>null</c> when unavailable.</summary>
    public double? Voltage { get; init; }

    /// <summary>
    /// Linear voltage approximation of state of charge, 0–100 inclusive, or
    /// <c>null</c> when unknown. Out-of-range values throw
    /// <see cref="ArgumentOutOfRangeException"/> at construction and on record
    /// <c>with</c> updates; the Domain never silently clamps.
    /// </summary>
    public int? Percent
    {
        get => _percent;
        init => _percent = value is int percent && percent is < 0 or > 100
            ? throw new ArgumentOutOfRangeException(
                nameof(Percent),
                value,
                "Battery percent must be between 0 and 100.")
            : value;
    }

    /// <summary>Level state derived from the reading; <see cref="BatteryState.Unknown"/> by default.</summary>
    public BatteryState State { get; init; } = BatteryState.Unknown;

    /// <summary>
    /// <c>true</c> only for the software simulator. A real sensor reading is
    /// always <c>false</c>; the UI labels simulated values.
    /// </summary>
    public bool Simulated { get; init; }

    /// <summary>
    /// Signed current in amps derived from the shunt voltage and the
    /// operator-supplied shunt resistance; <c>null</c> when unavailable. The
    /// sign is the module's raw shunt polarity — its charge/discharge meaning
    /// is a bench record (Task 42), never assumed here.
    /// </summary>
    public double? Current { get; init; }

    /// <summary>Signed power in watts (bus voltage × current); <c>null</c> when unavailable.</summary>
    public double? Power { get; init; }
}
