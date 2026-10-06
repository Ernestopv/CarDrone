# Battery Monitoring

## Purpose

Task 41 adds power/battery monitoring for the Raspberry Pi's own battery (the
UPS pack that feeds the Pi), exposing the measured pack voltage and a derived
state of charge through the control plane.

The sensor is an **INA219** (I2C, voltage/current/power shunt monitor) already
attached to the target Pi. This task implements the read-only monitoring path
only: a Raspberry I2C platform, an INA219 bus-voltage reader, a software mock
for PC development, a new control-plane endpoint `GET /api/battery`, and a
frontend panel that displays the reading.

Verification level delivered by this task: `implemented` / `built` /
`mock-tested` on the PC, plus Raspberry Pi runtime verification of the
I2C/INA219 read and the endpoint on the target. Physical battery/charger
electrical behaviour (state-of-charge accuracy, charger interaction, current
measurement) remains `NOT VERIFIED` unless separately recorded.

## Dependencies

- `specs/architecture/runtime-deployment.md` — decisions D3 (mode selected only
  at the DI composition root), D5 (strict config, fail fast, no silent
  fallback) and D4 (control plane vs media plane) govern this task. Battery is
  control-plane telemetry; it never mixes with the camera/media plane.
- `specs/hardware/camera-runtime.md` (Task 31) — the established
  `MODE=mock|<real>` + Raspberry platform gate + validated required config
  pattern that `BATTERY_MODE` mirrors. Its probe philosophy ("a monitoring
  probe never takes down the control plane") is reused for read failures.
- `specs/hardware/raspberry-hardware-provider.md` (Task 23) +
  `specs/hardware/gpio.md` (Task 25) — composition-root platform preflight and
  the convention that concrete Linux platforms live in Infrastructure and
  register only for the real mode.
- `docs/hardware/raspberry-pi-inventory.md` — target evidence for the I2C bus
  device, its group/gid and the host permission decision (single source of
  truth; no device path is invented here).
- `specs/backend/drone-api.md` / `specs/integration/frontend-api-service.md` —
  the additive-endpoint + service-adapter conventions. `DroneStatus`'s wire
  contract is **frozen**; battery is a separate endpoint, never a new field on
  the frozen drone status.
- `specs/backend/error-handling.md` — ProblemDetails error contract. This task
  adds no new exception→status mapping (a failed read is reported as an honest
  status object, not a 5xx).
- `specs/backend/backend-tests.md` — xUnit layout and determinism rules.
- `AGENTS.md` — hardware isolation (no pin/I2C addresses in Domain or React),
  "do not invent electrical behaviour", and "never claim a hardware reading is
  more than it is".

## Scope

- **Domain**: `BatteryState` enum and `BatteryStatus` record (voltage, optional
  percent, state, availability, simulated flag). No hardware vocabulary.
- **Application**: `IBatteryMonitor` read-only contract.
- **Infrastructure**:
  - `II2cBus` seam + `RaspberryI2cBus` concrete Linux implementation
    (`/dev/i2c-N` via libc `open`/`ioctl`/`read`/`write`, no new packages).
  - `Ina219BatteryMonitor`: reads the INA219 bus-voltage register, converts it
    to volts, derives a voltage-window state of charge.
  - `BatteryOptions`: validated configuration binding (`FromSection`).
  - `MockBatteryMonitor`: deterministic simulated reading for PC development.
- **Api**: `BatteryRuntimeSelection` composition-root selection
  (`BATTERY_MODE=mock|ina219`) and `BatteryController` (`GET /api/battery`).
- **Frontend**: `BatteryStatus` types, a `getBattery()` service method (API +
  mock implementations), a polling hook, and a `BatteryPanel` shown on the
  dashboard.
- **Deployment**: Pi Docker overlay device/group seam + native launcher
  environment passthrough for the I2C device, plus `.env`/runbook/inventory
  documentation.
- Backend and frontend tests, and the durable records (MEMORY, ARCHITECTURE,
  inventory).

## Out of Scope

- **Current and power measurement** (INA219 shunt/calibration registers): the
  shunt resistor value and expected current are unresolved physical facts; a
  later task may add them. This task reports voltage only.
- **Charging detection** and charger interaction/telemetry.
- **Coulomb counting / battery-curve state of charge**: the percent is a
  documented linear voltage approximation, not a fuel-gauge algorithm.
- Battery history logging, alerting, or persistence.
- Extending the frozen `DroneStatus` wire shape or the drone endpoints.
- Any actuation, safety, or fail-safe change (Task 26 semantics untouched).
- Making the reading part of `HARDWARE_MODE`; battery is an independent
  concern with its own `BATTERY_MODE`.

## Architecture

```text
React BatteryPanel
    ↓ (poll)
frontend DroneService.getBattery()
    ↓ HTTP GET /api/battery
BatteryController (Api, thin)
    ↓
IBatteryMonitor (Application)
    ↑
    ├── MockBatteryMonitor            (Infrastructure; BATTERY_MODE=mock)
    └── Ina219BatteryMonitor          (Infrastructure; BATTERY_MODE=ina219)
            ↓
        II2cBus
            ↑
        RaspberryI2cBus → /dev/i2c-N (libc P/Invoke)
            ↓
        INA219 (I2C bus voltage register)
```

Boundary rules (from AGENTS + D3):

- `BatteryStatus`/`BatteryState`/`IBatteryMonitor` carry **no** I2C addresses,
  device paths, register numbers, or INA219 vocabulary — those live only in
  Infrastructure configuration and the concrete implementation.
- React never learns a bus/address/register; it consumes the
  `/api/battery` JSON shape only.
- Selection happens once, at the composition root; Domain and Application
  never branch on platform or mode.

## Domain Model

```csharp
namespace DroneControl.Domain;

public enum BatteryState
{
    Unknown = 0,   // 0 is the pessimistic default (never reads as a healthy level)
    Ok,
    Low,
    Critical,
    Error,         // sensor reachable value unknown / read failed
}

public sealed record BatteryStatus
{
    public bool Available { get; init; }          // false = no valid reading
    public double? Voltage { get; init; }         // pack/bus volts, null when unavailable
    public int? Percent { get; init; }            // 0-100 linear approximation, null when unknown
    public BatteryState State { get; init; } = BatteryState.Unknown;
    public bool Simulated { get; init; }          // true only for the mock monitor
}
```

Default construction yields the honest "no reading" status:
`Available=false`, `Voltage=null`, `Percent=null`, `State=Unknown`,
`Simulated=false`.

`Percent`, when present, is constrained to `0..100`; out-of-range construction
throws `ArgumentOutOfRangeException` (never clamps), matching the Domain speed
invariant.

## Behavior

### Mode selection (`BatteryRuntimeSelection`)

- `BATTERY_MODE` unset → `mock`. Trimmed, case-insensitive accepted values:
  `mock`, `ina219`. Present-but-empty or any other value →
  `HostAbortedException` naming the key, the raw value and the accepted set
  (never a silent fallback — D5).
- `mock`: registers `IBatteryMonitor → MockBatteryMonitor`. The `Battery:`
  configuration section is never read.
- `ina219`: passes the Raspberry platform gate (Linux + ARM64 OS facts only,
  same internal `raspberryRuntimeSupported` test seam as Tasks 23/31) and
  requires a structurally valid `Battery:` configuration (below). Registers
  `II2cBus → RaspberryI2cBus` and `IBatteryMonitor → Ina219BatteryMonitor`.
- A `Battery:` section present while `BATTERY_MODE=mock` is ignored (mirrors
  the camera/hardware mock behavior).

### INA219 read

- Bus voltage register (`0x02`): bits 15..3 are the bus voltage in 4 mV LSBs;
  bit 1 is the conversion-ready flag and bit 0 the overflow flag.
  `voltageVolts = ((raw >> 3) * 4) / 1000.0`.
- The monitor performs **read-only** I2C transactions (it writes only the
  1-byte register pointer that every read requires). It never writes the
  configuration/calibration registers and never guesses the shunt value.
- Reading is on demand (per HTTP request); no background polling loop and no
  cached stale value in this task.
- `CancellationToken` is honored before the I2C transaction.

### State of charge

- With `FullVoltage` > `EmptyVoltage` configured:
  `percent = clamp(round((V - EmptyVoltage) / (FullVoltage - EmptyVoltage) * 100), 0, 100)`.
- `State` from the percent thresholds: `<= CriticalPercent` → `Critical`;
  `<= LowPercent` → `Low`; otherwise `Ok`.
- This is a **linear voltage approximation**, explicitly documented as such; it
  is not a fuel gauge and does not model the Li-ion discharge curve.

### Read failure

- A missing/inaccessible bus, a NACK, or a short read is caught and reported as
  `Available=false`, `Voltage=null`, `Percent=null`, `State=Error` — an honest
  status object with **HTTP 200**, never a fabricated voltage and never a
  5xx that would take down the control plane (same philosophy as the camera
  probe, D4). The failure is logged (transitions only where the monitor keeps
  state; otherwise once per failed request at Warning).

## Interfaces

```csharp
namespace DroneControl.Application;

public interface IBatteryMonitor
{
    Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
```

```csharp
namespace DroneControl.Infrastructure;

// Lowest I2C seam: word reads from a device register. Realized by
// RaspberryI2cBus (/dev/i2c-N); faked in tests. No INA219 knowledge here.
public interface II2cBus : IDisposable
{
    ushort ReadWord(int deviceAddress, byte register);
}
```

`Ina219BatteryMonitor(II2cBus bus, BatteryOptions options, ILogger? logger)`
is the only place that knows INA219 register `0x02` and the 4 mV LSB.

## Configuration

Read once at the composition root. Section `Battery:` (env prefix `BATTERY__`):

| Key | Required | Meaning |
| --- | --- | --- |
| `I2cBusPath` | mock: no; ina219: default `/dev/i2c-1` | I2C bus device node (target-observed default; overridable). |
| `I2cAddress` | ina219: **required** | 7-bit I2C address of the module (`0x03`–`0x77`). Operator value from the I2C probe. |
| `FullVoltage` | ina219: **required** | Pack voltage at 100 % (e.g. `8.4` for 2S Li-ion). |
| `EmptyVoltage` | ina219: **required** | Pack voltage at 0 % (e.g. `6.0`). Must be `< FullVoltage`. |
| `LowPercent` | optional (default `20`) | `Low` threshold. `0..100`, `>= CriticalPercent`. |
| `CriticalPercent` | optional (default `10`) | `Critical` threshold. `0..100`, `<= LowPercent`. |

Validation (`BatteryOptions.FromSection`) aggregates every violated rule into a
single `ArgumentException`; the composition root converts it to a
`HostAbortedException` before any registration (D5, no partial graph).

## Validation

- `I2cAddress` integer in `0x03..0x77`.
- `FullVoltage` finite and `> 0`; `EmptyVoltage` finite and `>= 0` and
  `< FullVoltage`.
- Thresholds integers in `0..100` with `CriticalPercent <= LowPercent`.
- A present `FullVoltage` without `EmptyVoltage` (or vice-versa) is invalid.
- Mock mode validates nothing (section unread), identical to an absent section.

## Error Cases

| Case | Result |
| --- | --- |
| Unknown/empty `BATTERY_MODE` | `HostAbortedException` at startup, accepted values named. |
| `ina219` on non-Linux/ARM64 | `HostAbortedException` naming the platform gate. |
| `ina219` with invalid/missing `Battery:` config | `HostAbortedException` with the aggregated reason, before registration. |
| Bus open fails / NACK / short read | `Available=false`, `State=Error`, **HTTP 200**; logged. |
| `Battery:` section in mock mode | Ignored; section never read. |

## Platform Requirements

- Backend builds/tests on the existing .NET 10 toolchain with **no Raspberry
  Pi and no new NuGet package** (libc P/Invoke only).
- `RaspberryI2cBus` targets `linux/arm64` (and any Linux exposing `/dev/i2c-N`);
  the mode gate keeps PC builds on the mock.
- Docker Pi overlay maps the I2C device and group only from recorded target
  evidence; the native launcher (root) accesses the bus directly. PC mode
  remains device-free.

## Security / Safety

- Read-only: this task cannot actuate anything and touches no motor/GPIO/PWM
  path. It introduces no safety-state change.
- Honesty: the mock reading is marked `simulated=true`; the frontend labels it;
  a sensor reading is never presented as more than a measured bus voltage with
  a linear approximation. No "battery health"/"charging" claim is made.
- The container remains non-root; access is granted by the least-privilege
  device + group mapping recorded in the inventory.

## Testing Scenarios

Backend (`DroneControl.Application.Tests`, Infrastructure implementations):

1. `BatteryOptions.FromSection`: valid binding; each invalid rule (address
   range, full/empty relation, threshold ordering, missing half of the window)
   throws with the reason; defaults for thresholds.
2. `Ina219BatteryMonitor` over a **fake `II2cBus`**: known raw bus-voltage word
   → expected volts; percent/state at Empty/Low/Critical/Full boundaries;
   read exception → `Available=false, State=Error`; cancellation honored;
   read-only (the fake records zero writes).
3. `MockBatteryMonitor`: deterministic, `Simulated=true`, valid ranges.

Backend (`DroneControl.Api.Tests`):

4. `BatteryRuntimeSelection`: unset → mock graph; invalid value → abort;
   `ina219` with the internal platform seam true + valid config → ina219
   registrations; `ina219` on a non-Raspberry runtime → abort; invalid config →
   abort before registration.
5. `GET /api/battery` with a fake `IBatteryMonitor` override: 200 + camelCase
   JSON of the status; error status serializes with `available:false`.

Frontend (Vitest):

6. `apiDroneService.getBattery` maps the wire shape (including nulls and
   `simulated`).
7. `MockDroneService.getBattery` returns a valid simulated status.
8. `useBatteryMonitor` polls and cleans up; a rejection yields the unavailable
   state without throwing.
9. `BatteryPanel` renders voltage/percent/state, the unavailable state, and the
   simulated badge.

## Acceptance Criteria

- [x] Domain gains `BatteryState` and `BatteryStatus` with the honest default
      status; `Percent` rejects out-of-range instead of clamping; no
      I2C/device/register/INA219 vocabulary exists in Domain or Application.
- [x] `IBatteryMonitor` is the only Application contract; controllers consume
      it and own no I2C logic.
- [x] `BatteryRuntimeSelection` selects `mock` (unset) or `ina219` (after the
      linux/arm64 gate + validated `Battery:` config) exactly like
      `CAMERA_MODE`; unknown/empty values and invalid config abort startup with
      explicit messages (D5); mock mode never reads the `Battery:` section.
- [x] `RaspberryI2cBus` performs word reads over `/dev/i2c-N` via libc P/Invoke
      with no new package; `Ina219BatteryMonitor` reads register `0x02`, applies
      the 4 mV LSB, and is read-only.
- [x] `GET /api/battery` returns HTTP 200 with the `BatteryStatus` JSON
      (camelCase, lowercase enum strings); a read failure returns HTTP 200 with
      `available:false` / `state:error` and is logged.
- [x] `BATTERY_MODE=mock` yields a deterministic simulated status with
      `simulated:true`; the same graph as an absent key.
- [x] Frontend exposes `getBattery()` on `DroneService` (API + mock), polls it
      without leaking timers, and renders `BatteryPanel` with voltage, percent,
      state, an unavailable state, and a simulated marker.
- [x] `docker compose config` on the PC remains device-free two-service output;
      the Pi overlay maps `${I2C_DEVICE}`/`${I2C_GID}` only from recorded
      evidence, and the native launcher passes `Battery__*` env to the backend.
- [x] Backend and frontend test suites pass on two consecutive runs; `dotnet
      build` 0 warnings/0 errors; frontend build/lint clean.
- [x] Raspberry Pi runtime: the I2C device/GID and the INA219 address are
      recorded in `docs/hardware/raspberry-pi-inventory.md`; the endpoint
      returns the measured pack voltage on the target. Physical
      state-of-charge/charger behaviour is explicitly recorded `NOT VERIFIED`
      (never claimed).

## Open Questions

1. **INA219 I2C address — RESOLVED (2026-10-06).** The read-only register probe
   on the target observed a device at **`0x42`** (config `0x399F`, the INA219
   default; no overflow) with `bus_voltage_raw=0x341A` = **6.668 V**. This is
   operator evidence recorded in `docs/hardware/raspberry-pi-inventory.md`
   (section "I2C") and supplied as `BATTERY_I2C_ADDRESS=0x42`. The module
   default `0x40` was **not** assumed and would have been wrong.
2. **Battery window** — the operator states 2×18650 Li-ion in series with an
   8.4 V/2 A charger (2S: 8.4 V full, 6.0 V empty). These are the operator's
   values for `FullVoltage`/`EmptyVoltage`; they are recorded as operator
   configuration, not measured by this task.
3. **Where the INA219 sits electrically** (battery terminals vs charger/UPS
   output) — recorded in the hardware record as `NOT VERIFIED` until observed.
   The probe's 6.668 V reading is taken as measured, without a wiring claim.

## Verification result

**Task 41 complete — verified on the real target (Raspberry Pi 4 / Ubuntu
22.04, Docker Compose Pi mode, 2026-10-06).**

- Probe (host, root): INA219 at **`0x42`** (config `0x399F`, no overflow),
  `bus_voltage_raw=0x341A` = **6.668 V**; shunt/calibration registers 0
  (current/power not meaningful — out of scope).
- Deployed Docker stack: non-root backend container (supplementary gid 119)
  reads `/dev/i2c-1` (no host udev change); `GET /api/battery` returned
  `{"available":true,"voltage":6.472,"percent":20,"state":"low","simulated":false}`
  directly on `:5080` and through nginx `:8081` (`BATTERY_MODE=ina219`,
  `BATTERY_I2C_ADDRESS=0x42`, `BATTERY_FULL_VOLTAGE=8.4`,
  `BATTERY_EMPTY_VOLTAGE=6.0`, `I2C_DEVICE=/dev/i2c-1`, `I2C_GID=119`).
- The pack read ≈6.47 V / 20 % (at the configured `Low` threshold) at the time
  of validation — the earlier probe read 6.668 V ≈28 %, so the pack was
  draining.
- Software: backend 419 tests ×2, frontend 157, `dotnet build` + linux-arm64
  build 0/0, compose config PC/Pi rendered correctly.
- Remaining `NOT VERIFIED` (explicit, never claimed): physical
  state-of-charge accuracy, charger interaction, and current/power (shunt).
