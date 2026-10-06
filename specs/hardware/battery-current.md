# Battery Current & Power (INA219 shunt)

## Purpose

Task 42 extends the Task 41 battery monitor (`specs/hardware/battery-monitoring.md`)
with **current (A)** and **power (W)**. The INA219 measures the shunt voltage
across a 0.1 Ω (R100) resistor; the backend computes `current = V_shunt /
R_shunt` and `power = V_bus × current` in software, so the sensor stays
**read-only** (the calibration register remains 0; no register is written
beyond the one-byte pointer of a read).

The shunt value is an operator-confirmed hardware fact: **R100 = 0.1 Ω**
(2026-10-06). The probe evidence (`shunt_raw=0xE2DF` → −74.57 mV → −0.746 A at
6.668 V → −4.97 W) is a consistency check for the deployed endpoint.

Verification level delivered: `implemented` / `built` / `mock-tested` on the PC
plus **Raspberry Pi runtime verification** of `GET /api/battery` with
`BATTERY_SHUNT_OHMS=0.1`. The charge/discharge **sign convention** is recorded
from bench observation only; nothing is assumed.

## Dependencies

- `specs/hardware/battery-monitoring.md` (Task 41, base) — the
  `BATTERY_MODE=mock|ina219` selection, `BatteryOptions.FromSection`
  validation, the read-only `Ina219BatteryMonitor`, the honest unavailable
  semantics, the `GET /api/battery` wire shape and the mock labelling. This
  spec extends those surfaces; it reuses the base contracts unchanged where
  possible.
- `docs/hardware/raspberry-pi-inventory.md` (section "I2C") — the INA219
  address evidence (`0x42`), the measured bus voltage, and now the operator
  shunt confirmation.
- `docs/ARCHITECTURE.md` / `docs/DECISIONS.md` — D3 (composition-root mode
  selection), D5 (strict config, fail fast).

## Scope

- Domain: add `Current` and `Power` (both `double?`, volts-derived) to
  `BatteryStatus`. No hardware vocabulary; the plain default remains the honest
  "no reading" status with both fields `null`.
- Infrastructure:
  - `BatteryOptions.ShuntOhms` — new required operator value in `ina219` mode
    (positive finite), aggregated with the existing validation.
  - `Ina219BatteryMonitor` — also reads the shunt-voltage register (`0x01`,
    signed 16-bit, 10 µV LSB); `current = shuntVolts / ShuntOhms`;
    `power = busVolts × current`. Still read-only, still failure-safe
    (a read failure keeps the unavailable/error status).
  - `MockBatteryMonitor` — deterministic simulated current/power, still
    `simulated:true`.
- Api: no surface change beyond the config pass-through
  (`Battery:ShuntOhms`); `GET /api/battery` starts including the two fields.
- Frontend: `BatteryStatus.current` / `.power`, `BatteryPanel` rows, and
  normalization.
- Backend + frontend tests, deployment config (Compose + native), inventory and
  durable records.

## Out of Scope

- Writing the INA219 calibration register or reading its internal current/power
  registers (this task computes from the raw shunt register instead).
- Coulomb counting / integrating current into state of charge.
- Charging-state detection (the sign is reported as measured; the
  charge-vs-discharge meaning is a bench record, not a computed label).
- Any change to the frozen drone contract or the camera/media plane.

## Architecture

```text
BatteryController (GET /api/battery)  — unchanged surface, richer body
    ↓
IBatteryMonitor
    ↑
Ina219BatteryMonitor                       MockBatteryMonitor (simulated)
    ↓ reads registers 0x02 (bus) + 0x01 (shunt)
II2cBus → RaspberryI2cBus → /dev/i2c-1
    ↓
current = shuntVolts / ShuntOhms          power = busVolts × current
```

## Domain model changes

```csharp
public sealed record BatteryStatus
{
    // ...existing members...
    public double? Current { get; init; }   // signed amps (null when unavailable)
    public double? Power { get; init; }     // signed watts (null when unavailable)
}
```

- Default construction keeps both `null`.
- The two fields are measured values only; no clamping or rounding is done in
  the Domain (rounding/formatting is a presentation concern).

## Configuration

New section key in `Battery:`:

| Key | Required | Meaning |
| --- | --- | --- |
| `ShuntOhms` | ina219: **required** | Shunt resistance of the INA219 board (operator evidence; `0.1` for a `R100` module). Positive finite number; absent/invalid/precision-losing value aborts startup. |

`BatteryOptions.FromSection` now also validates `ShuntOhms` (positive, finite,
`> 0`, parseable with invariant culture) and aggregates it with the existing
rules. A voltage-only deployment is no longer possible under `ina219`
(current/power are part of the reading now); `BATTERY_MODE=mock` still never
reads the section.

## Behavior

- `Ina219BatteryMonitor.GetStatusAsync` performs the existing bus-voltage read
  **and** a shunt-voltage read on the same request. Either failure yields the
  existing unavailable/error status (HTTP 200), never a partial fabricated
  reading.
- Shunt-voltage register: signed 16-bit two's complement, LSB 10 µV
  (`shuntVolts = (short)raw × 10e-6`). Negative values are reported as negative.
  `current = shuntVolts / ShuntOhms`; `power = busVolts × current`.
- The sign is the module's raw polarity. The operator's bench record
  (2026-10-06): **negative = charging, positive = discharging** for this
  deployment. The frontend shows a `CHARGING`/`DISCHARGING` hint derived from
  the sign (per that confirmed convention) next to the signed magnitude; the
  signed value itself is always the raw measurement — never re-labelled on the
  wire.

## Validation

- `ShuntOhms`: required; `double` parseable with invariant culture; `> 0` and
  finite; rejects `0`, negatives, NaN/Infinity, and malformed text, with the
  reason included in the aggregated `ArgumentException`.
- `BatteryStatus.Current`/`Power` are nullable doubles with no domain
  constraints.

## Error Cases

| Case | Result |
| --- | --- |
| `BATTERY_MODE=ina219` without `Battery:ShuntOhms` | `HostAbortedException` (aggregated with other violations), before registration. |
| `ShuntOhms <= 0` / malformed | Same abort; reason names `Battery:ShuntOhms`. |
| Shunt read fails while bus read succeeds (or vice-versa) | The whole call reports unavailable/error — no partial reading. |

## Platform Requirements

- No new NuGet packages; `RaspberryI2cBus` is unchanged (the monitor already
  reads words).
- Builds/tests on the existing PC toolchain; linux-arm64 build stays clean;
  the Pi Docker stack reads `/dev/i2c-1` with gid 119 as before.

## Security / Safety

- Read-only: no sensor register is ever written; no actuation surface.
- Honesty: current/power are computed from the raw shunt register and the
  operator shunt value; the sign convention is a bench record, and the mock
  stays labelled `simulated`.

## Testing Scenarios

Backend (Application.Tests):

1. `BatteryOptions` with `ShuntOhms` valid (`0.1`) → bound; missing / zero /
   negative / malformed / NaN → aggregated error naming `ShuntOhms`.
2. `Ina219BatteryMonitor` over a fake bus returning known bus + shunt words:
   - shunt `0x0000` → `Current 0`, `Power 0`;
   - shunt `0xE2DF` (−7457 → −74.57 mV) with R 0.1 Ω and bus 6.668 V →
     `Current ≈ -0.7457`, `Power ≈ -4.9735`;
   - positive shunt (e.g. `0x1D21` = 7457 → +0.7457 A) → positive values;
   - shunt read failure (either register) → unavailable/error, no partial data.
3. `MockBatteryMonitor` still deterministic and `simulated:true`, now with
   current/power.

Backend (Api.Tests):

4. Composition: `ina219` without `ShuntOhms` aborts; with it, the graph
   registers as before.
5. Wire: `GET /api/battery` JSON includes `current` and `power`.

Frontend:

6. apiDroneService maps current/power; normalizes non-numeric to `null`.
7. Panel renders current/power rows; simulated values labelled.

## Acceptance Criteria

- [x] `BatteryStatus` gains `Current` and `Power` (`double?`, default `null`);
      `BatteryOptions.ShuntOhms` is a required, validated operator value in
      `ina219` mode (positive finite; absent/invalid aborts naming the key).
- [x] `Ina219BatteryMonitor` reads the shunt register and computes
      `current = shuntVolts / ShuntOhms` and `power = busVolts × current` with
      the documented LSBs, remaining read-only (the fake bus records zero
      writes).
- [x] A failed read (bus or shunt) still yields the honest unavailable/error
      status with HTTP 200 — never a partial reading.
- [x] `GET /api/battery` includes `current` and `power` (camelCase, null when
      unavailable); mock reports deterministic simulated values with
      `simulated:true`.
- [x] `BatteryPanel` shows current/power rows and the unavailable/simulated
      states honestly.
- [x] Compose/native config pass `Battery__ShuntOhms` (env
      `BATTERY_SHUNT_OHMS`); `docker compose config` renders correctly on PC
      and Pi.
- [x] Backend and frontend suites pass on two consecutive runs; build/lint
      clean; linux-arm64 build succeeds.
- [x] Raspberry Pi runtime: with `BATTERY_SHUNT_OHMS=0.1`, `GET /api/battery`
      returns consistent current/power (the probe's −74.57 mV → ≈−0.75 A at
      ≈6.47 V → ≈−4.8 W as a consistency check); the charge/discharge sign
      convention is recorded `NOT VERIFIED` unless bench-observed.

## Sign-convention record (operator-confirmed)

The INA219 shunt-voltage sign maps to charge/discharge according to the module
wiring. **Operator bench observation (2026-10-06): negative current =
charging, positive current = discharging** for this deployment's polarity.
The endpoint reports the **signed** value as measured; the frontend panel adds
a `CHARGING`/`DISCHARGING` hint from that sign per the confirmed
convention (both derived in the UI, never on the wire; a zero/unknown current
shows no hint).

## Verification result

**Task 42 complete — verified on the real target (Raspberry Pi 4 / Ubuntu
22.04, Docker Compose Pi mode, 2026-10-06).**

- Deployed with `BATTERY_SHUNT_OHMS=0.1` (plus the Task 41 operator values).
  `GET /api/battery` now includes `current` and `power`:
  `{"available":true,...,"current":-1.03,"power":-6.38}` (and through nginx
  `:8081`).
- Continuous load characterisation (five 1 s samples): **≈6.17 V, ≈−1.0 to
  −1.12 A, ≈−6.3 to −6.9 W**, ≈7 %. With the operator-confirmed convention
  (negative = charging) the signed ≈−1.1 A is a **charge current ≈1.0–1.1 A**
  (≈6.5 W charging the pack) while serving the Pi load; the pack voltage was
  low (≈6.17 V, ≈7 % — charging recommended, the pack had been low).
- **Formula equivalence (operator reference)** — the user's Python example
  (Adafruit-style) computes `percent = clamp((V - 6) / 2.4 × 100, 0, 100)` with
  `FullVoltage 8.4 V / EmptyVoltage 6.0 V`; this implementation uses the same
  linear formula with the same window, so the percentages match. For current,
  the reference writes the calibration register (0x05 = 4096, 100 µA/bit) and
  reads the chip's current/power registers; this implementation keeps the
  sensor read-only and computes `I = V_shunt / R_shunt`
  (10 µV / 0.1 Ω = 100 µA per LSB) — **the same physical value**. (The
  reference's `value -= 65535` sign conversion is off by one LSB; this
  implementation uses a correct 16-bit two's-complement cast.)
- **Shunt-range saturation (honest limitation)**: the INA219's default PGA
  gain (config `0x399F`) reads a ±320 mV shunt — at R100 that is **±3.2 A**.
  One earlier sample read exactly `current:3.2` (the ±320 mV clip) during a
  load transient; beyond ±3.2 A the reading saturates. A future task could
  write a lower PGA gain or use a smaller shunt; this task deliberately keeps
  the sensor read-only.
- **Sign convention**: **CONFIRMED by operator bench observation (2026-10-06):
  negative = charging, positive = discharging**. The stable reading (≈−1.1 A,
  ≈−6.5 W) is therefore a **charge** of ≈1.0–1.1 A / ≈6.5 W. The signed values
  remain as measured on the wire; the meaning is this record, never recomputed.
- Software: backend 427 tests ×2, frontend 157, `dotnet build` + linux-arm64
  build 0/0, compose config PC/Pi rendered correctly.