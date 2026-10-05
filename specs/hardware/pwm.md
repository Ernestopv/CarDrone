# PWM Speed Control (Task 30)

## Purpose

Task 30 maps the application speed value (`0`–`100`) to a PWM duty cycle for the
configured PWM identifiers (`GPIO:PWM1`, `GPIO:PWM2` — known configuration
values `12` and `13`), behind an Infrastructure-only PWM seam:

```text
Requested Speed (0-100)
      ↓
SpeedController (speed → duty mapping)
      ↓
IPwmController (output seam)
      ↓
  ┌───┴────────────────────────────┐
  dry-run: record + suppress       real: platform adapter
  (no physical sink)               (concrete platform deferred)
      ↓
configured PWM identifiers (PWM1 / PWM2)
```

The task defines the mapping, the abstraction, validation, activation rules,
tracking of `requestedSpeed`/`appliedSpeed`, and honest behavior in all three
runtime modes. It does **not** choose a Linux PWM subsystem, does **not**
invent any electrical value (frequency, duty envelope, electrical limits), and
does **not** enable physical actuation: every value comes from operator-supplied
configuration behind an assertion flag, exactly as Task 29 did for direction.

Verification level delivered by this task: `implemented` / `built` /
`mock-tested` / `dry-run-tested` on the development PC. Nothing
Raspberry-Pi-runtime or physical is claimed (prerequisites remain owned by the
prerequisites ledger and later hardware tasks).

## Dependencies

- `AGENTS.md` — GPIO configuration values (including `PWM1:12`, `PWM2:13`)
  treated **only as configuration**; layering rules (controllers never touch
  GPIO/PWM, Domain never depends on Infrastructure); hardware honesty rules;
  "do not implement real PWM control before the hardware layer is designed".
- `specs/architecture/runtime-deployment.md` (binding) — decisions D1/D3/D5/D7,
  runtime modes, verification levels, and the prerequisites ledger:
  - the PWM interface row ("which subsystem drives GPIO12/GPIO13, API, clock
    source") is jointly owned by Tasks 24/30: Task 30 owns defining the seam;
    the concrete subsystem/device/clock selection stays an **open prerequisite**
    and must not be invented here.
  - the PWM frequency/safe duty-envelope row remains a physical unknown owned
    by Task 30: the task may only define *where operators record asserted
    values*, never the values themselves.
- `specs/hardware/hardware-abstraction.md` — `IDroneHardware.ApplySpeedAsync`
  is the Application-facing seam; speed success is a software result, never a
  physical acknowledgement; `MockDroneHardware` stays test-only.
- `specs/hardware/gpio.md` — the GPIO seam stays digital-only ("it does not
  open PWM interfaces"); `PWM1`/`PWM2` are currently unconsumed there and are
  consumed by Task 30 through its own binding; duplicate-identifier rejection
  convention.
- `specs/hardware/motor-control.md` (Task 29) — the activation-matrix,
  assertion-flag, strict-validation, and real-mode-abort precedents that this
  task mirrors; Task 29's "speed stays unsupported" behavior is what Task 30
  replaces.
- `specs/hardware/dry-run.md` — the output boundary, the operation record
  shape (`HardwareOutputCategory.Pwm` was reserved for exactly this task),
  `Available ≠ physical`, "Commands and speed" rules (range validation uses
  the same path in every mode; dry-run must not bypass safety transitions).
- `specs/hardware/raspberry-hardware-provider.md` — the inert/active provider
  behavior, `HardwareDroneController` flow (range check first, disconnected =
  silent software no-op, `_speedPercent` records requested speed), and the
  frozen disconnect semantics (speed 0 achieved by forgetting state, **no**
  hardware call).
- `specs/hardware/failsafe.md` (Task 26) — `SetSpeedAsync` state gate
  (`Safe`/`CommandActive`), bounded timeout, and failure transitions (STOP
  attempt → `Safe`/`Faulted`). Reused unchanged; Task 30 does not redefine
  safety transitions.
- `specs/hardware/motor-wiring.md` (Task 28) — mandates that this
  specification list `docs/hardware/WIRING.md` as a dependency (done below)
  and forbids inventing PWM frequency/duty values.
- `docs/hardware/WIRING.md` — the evidence record for the assertion flag. The
  `ENA`/`ENB` control rows and the "does `PWM1`/`PWM2` reach `ENA`/`ENB` at
  all" fact remain `NOT VERIFIED`.
- `specs/backend/backend-tests.md` and the dotnet skill — test placement and
  conventions (Hardware/Provider/DI wiring → Api tests; pure logic →
  Application tests).
- The `raspberry-pi` skill is **not available** in this environment (loading
  it fails; precedent set in `hardware-abstraction.md`). Hardware rules are
  enforced through this specification, `AGENTS.md`, and the prerequisites
  ledger instead. No physical PWM behavior is inferred anywhere below.

## Scope

Infrastructure + composition root only:

- A validated `PwmMapping` configuration type bound from a new
  `PwmMapping` configuration section plus the existing `GPIO` section
  (`PWM1`/`PWM2`), using the same strict binding style as `MotorMapping`
  (case-insensitive keys, no default electrical values, all violations
  aggregated into one explicit startup failure).
- An `IPwmController` output seam in Infrastructure with:
  - `DryRunPwmController` — records and suppresses every intended PWM
    operation (`HardwareOutputCategory.Pwm`); structurally holds no physical
    sink;
  - `IRaspberryPwmPlatform` port + `RaspberryPwmController` adapter —
    validation + forwarding over a small isolated port, tested with a fake
    platform only; the concrete Linux implementation is explicitly deferred.
- A mode-agnostic `SpeedController` that resolves the duty from the mapping,
  drives both PWM identifiers in a deterministic order, tracks
  `requestedSpeed`/`appliedSpeed`, and defines shutdown behavior.
- `RaspberryDroneHardware.ApplySpeedAsync` active path (delegates to
  `SpeedController`) plus capability-specific inactive failure messages.
- Composition-root activation matrix extension in `DroneRuntimeSelection`
  (mirroring Task 29): section absent / validated-but-unasserted / asserted /
  invalid, per mode; extension of the unsafe-composition guard to reject real
  PWM sinks in dry-run graphs; registration via an explicit factory so any
  combination of active controllers is expressible.
- Documentation deliverables: a `PWM speed envelope (Task 30)` evidence
  section in `docs/hardware/WIRING.md`, stale-message/comment corrections
  (enumerated below), and durable-doc updates (`MEMORY.md`,
  `docs/ARCHITECTURE.md`).

## Out of Scope

- Any concrete Linux PWM subsystem selection: device paths, driver/library
  choice, clock source, chip/channel numbers on the operating system. Deferred
  (prerequisites ledger); `RaspberryPwmController` is proven only against a
  fake platform.
- Physical facts: whether `PWM1`/`PWM2` connect to `ENA`/`ENB`, what duty is
  safe for the motors/L298N, what frequency is acceptable, motor response per
  speed. All remain `NOT VERIFIED`; recorded only by operator assertion.
- Any shipped default value for frequency or duty envelope. The default
  configuration contains **no** `PwmMapping` section at all.
- Wire/API/Domain/Application/frontend changes: the HTTP contract keeps a
  single `speed` field (the requested percentage). `appliedSpeed` is
  Infrastructure-internal (user decision: tracking stays out of the wire; the
  frontend must not see PWM vocabulary).
- `Mock` mode behavior: the simulated speed pipeline is untouched; the mock
  graph never reads `PwmMapping`.
- Safety transitions, STOP semantics, timeout policy, session lifecycle
  (Tasks 26/23 own these; reused as-is).
- uStreamer/camera, compose changes, Docker device/permission changes,
  privileged containers.
- A production `MockPwmController` type: tests use local doubles (the
  production sinks are dry-run and the deferred real adapter).

## Architecture

Preserved layering:

```text
React (unchanged)                     ← no PWM vocabulary
ASP.NET Core API / Application        ← unchanged (ApplySpeedAsync contract)
HardwareDroneController               ← existing; range check, session rules
RaspberryDroneHardware (provider)     ← chooses capability behavior
   ├── MotorController   (Task 29)    → IGpioController (digital, direction)
   └── SpeedController   (Task 30)    → IPwmController   (duty, speed)
                                          ├── DryRunPwmController  (dry-run)
                                          └── RaspberryPwmController
                                                → IRaspberryPwmPlatform (deferred)
```

- `IPwmController` lives beside `IGpioController` in Infrastructure. The GPIO
  seam is not extended: `gpio.md` fixes it as digital-only, and dry-run needs
  a distinct record category (`HardwareOutputCategory.Pwm`) anyway.
- Only Infrastructure and the composition root may mention `PWM`, `duty`,
  `PwmMapping`, or platform identifiers. Domain, Application, API
  controllers, and the frontend stay free of PWM vocabulary (greppable rule,
  same as Task 29).
- Mode selection stays at the composition root. `SpeedController` contains no
  mode/flag/branching logic (mirrors `MotorController`): composition decides
  whether it exists at all.

## Domain Model

### `PwmMapping` (Infrastructure, section `PwmMapping`)

Bound by a factory `FromSection(...)` in the `MotorMapping` style: it
validates **all** content whenever the section exists (even when unasserted)
and returns `null` when the flag is `false` (validated but unused).

```jsonc
"PwmMapping": {
  "FrequencyHz": "<positive integer — operator-supplied>",
  "MinDutyPercent": "<0-100 — operator-supplied>",
  "MaxDutyPercent": "<0-100, >= Min — operator-supplied>",
  "SpeedMappingVerified": false
}
```

Exposed shape (normative meaning, names are implementation detail):

| Member | Meaning |
|---|---|
| `FrequencyHz` | Operator-supplied PWM frequency in Hz; integer `> 0`. No electrical upper bound is asserted by software. |
| `MinDutyPercent` | Minimum usable duty percent for non-zero speeds; `0`–`100`. |
| `MaxDutyPercent` | Maximum duty percent; `0`–`100`, `>= MinDutyPercent`. |
| `Identifiers` | The configured PWM identifiers from the `GPIO` section, in `PWM1`, `PWM2` order. |
| `SpeedMappingVerified` | Operator assertion flag (default `false`). Assertion ≠ proof, same D7-style semantics as `DirectionMappingVerified`. |

Identifiers are read from the existing `GPIO` section: `PWM1`/`PWM2` must be
present, parse as positive integers, be distinct from each other, and not
collide with `Pin1`–`Pin4` (the duplicate-identifier rule of `gpio.md`).
When the `PwmMapping` section exists, a valid `GPIO` section is required —
the same explicit "requires a valid `GPIO` section" abort rule Task 29
established for `MotorMapping`.

### Speed → duty mapping (defined by this task; no electrical claim)

```text
speed = 0        → duty = 0            (always; ignores Min/Max)
speed = 1..100   → duty = MinDutyPercent
                        + ((MaxDutyPercent - MinDutyPercent) * speed) / 100

integer arithmetic, truncated toward zero
```

Consequences (all test-pinned): `speed = 100` yields exactly
`MaxDutyPercent`; `speed = 1` yields `MinDutyPercent` whenever
`Max - Min < 100` (truncation); `Min = Max` maps every non-zero speed to the
same duty; `Min = Max = 0` (an explicit operator choice) yields duty `0` for
every speed. The formula is the simplest defensible mapping; the backlog asks
the task to *define* the mapping, and any future mapping shape would be a
configuration/spec change, not an implicit behavior.

### `IPwmController` (Infrastructure seam)

```csharp
public interface IPwmController : IDisposable
{
    void ConfigureOutput(int identifier, int frequencyHz);
    void SetDutyCycle(int identifier, int dutyPercent);
}
```

Shape mirrors `IGpioController` (configure-then-write); exact naming is an
implementation detail within these meanings. The seam is synchronous,
mode-agnostic, and knows nothing about speed, motors, L298N, or commands.

### `SpeedController` (Infrastructure)

- Depends on `IPwmController` + `PwmMapping` only.
- `Apply(int speedPercent, CancellationToken)`:
  1. range check `0`–`100` first (`ArgumentOutOfRangeException`) — the
     reject-never-clamp invariant holds in every mode and configuration;
  2. cancellation check before anything is driven;
  3. compute the single duty for all identifiers;
  4. resolve/validate everything before touching the sink, then drive the
     deterministic sequence: `ConfigureOutput(PWM1, f)`,
     `ConfigureOutput(PWM2, f)`, `SetDutyCycle(PWM1, d)`,
     `SetDutyCycle(PWM2, d)`;
  5. only after all four operations complete, update tracking.
- Tracking (Infrastructure-only; user decision): `LastRequestedSpeedPercent`
  (`0`–`100`) and `LastAppliedDutyPercent` (duty percent actually issued) —
  `int?`, `null` until first success. On failure they keep their previous
  values (never half-updated, never silently reset). This is the backlog's
  `requestedSpeed`/`appliedSpeed`, with the unit difference made explicit:
  requested is speed percent, applied is duty percent.
- `Dispose()` implements shutdown behavior (below).

### `DryRunPwmController` (dry-run sink)

Mirrors `DryRunGpioController`: validates against the configured identifiers,
records each intended operation, then suppresses it. It holds no reference to
any physical sink, so a dry-run graph is structurally incapable of a PWM
write. Record formats (fixed for deterministic tests, culture-invariant):

```text
configure frequency-hz={frequencyHz}
write duty-percent={dutyPercent}
```

- identifier = configured PWM identifier as a decimal string (e.g. `12`),
  matching how GPIO records carry the pin number;
- category = `HardwareOutputCategory.Pwm` (the vocabulary `dry-run.md`
  reserved for this task — its "reserved / never emitted yet" comments are
  corrected as part of this task);
- valid intended operations carry the suppression reason; rejected operations
  (unknown identifier, `frequencyHz <= 0`, duty outside `0`–`100`, write
  before configure) carry the rejection reason and then throw with the same
  exception contract as the GPIO boundary (`ArgumentException` /
  `InvalidOperationException` / `ArgumentOutOfRangeException` as
  appropriate); `ObjectDisposedException` after disposal;
- invariants hold unchanged: `DryRun=true`, `PhysicallyApplied=false`,
  sequence increments, `Suppressed=true`;
- a failure of the record callback propagates (fail-closed), exactly like the
  GPIO boundary.

### `IRaspberryPwmPlatform` + `RaspberryPwmController` (real-side seam, deferred)

- The port is a small, library-free interface (`ConfigureOutput`,
  `SetDutyCycle` over the configured identifiers) — the isolated Linux
  boundary where a PWM subsystem will later attach. The concrete
  implementation (subsystem, device paths, clock source) is **deferred to the
  prerequisites ledger** and must not be invented here.
- `RaspberryPwmController` mirrors `RaspberryGpioController`: validates
  identifier membership, `frequencyHz > 0`, duty `0`–`100`, and
  configure-before-write; forwards unchanged; propagates platform failures;
  disposal follows the GPIO adapter's contract. Proven with a fake platform
  in tests only.

## Behavior

### Activation matrix (hardware-backed modes: `dry-run`, `real`)

Exactly the Task 29 matrix, applied to `PwmMapping`:

| `PwmMapping` section | Startup result | Registered |
|---|---|---|
| Absent (shipped default) | Startup succeeds | Nothing PWM-related (`SpeedController` not registered) |
| Present, content invalid, any flag | **Aborted** with the full aggregated error list, before any registration | Nothing |
| Present, valid, `SpeedMappingVerified=false` | Startup succeeds (validated but unused) | Nothing |
| Present, valid, `SpeedMappingVerified=true`, `dry-run` | Startup succeeds; **active** | `PwmMapping`, `IPwmController` → `DryRunPwmController`, `SpeedController` |
| Present, valid, `SpeedMappingVerified=true`, `real` | **Activated** — `RaspberryPwmPlatform` + `RaspberryPwmController` (concrete sysfs PWM sink) registered; requires `Raspberry:PWM:ChipPath` + per-identifier channels from evidence, else `HostAbortedException` naming the missing config | NULL-until-validated; speed applied through the real sink |

- `mock`: the section is never read; registrations are identical to a graph
  without the section (simulated speed unchanged).
- `HARDWARE_MODE=real` remains double-gated: the D7 external-protection gate
  (default `false`) still aborts first, and an asserted `PwmMapping` also
  aborts even if D7 were asserted — an assertion is never served inert under
  `real`, and never fabricated as physical.
- An asserted `PwmMapping` combined with an unasserted/invalid `MotorMapping`
  does not soften Task 29's rules: each section is validated and gated
  independently.

### Provider capability matrix

`RaspberryDroneHardware` receives `MotorController?` and `SpeedController?`.
Registration uses an explicit composition-root factory (resolving both
controllers with `GetService`) because DI constructor selection cannot express
the speed-only combination; the parameterless and `MotorController`-only
constructors remain for existing direct-construction call sites (no
pre-existing `new RaspberryDroneHardware(...)` usage changes).

| `MotorController` | `SpeedController` | Availability | Commands | Speed |
|---|---|---|---|---|
| — | — | `Unavailable` | default `DroneUnavailableException` (established message) | default `DroneUnavailableException` (established message) |
| active | — | `Available` | applied (Task 29) | specific reason: speed not configured — requires a valid asserted `PwmMapping` |
| — | active | `Available` | specific reason: direction not configured — requires a valid asserted `MotorMapping` | applied |
| active | active | `Available` | applied | applied |

- The exception **type** is `DroneUnavailableException` in every inactive
  cell (behavior contract unchanged); only the message differs: when the
  provider is `Available` but one capability is missing, the message must
  name that capability and the section/flag required to enable it. The fully
  inert provider keeps the established default message verbatim.
- `GetAvailabilityAsync` = `Available` iff at least one controller is present.
  All pre-existing availability tests exercise the both-absent row (unchanged)
  or the motor-active row (still `Available`).
- Range validation, connection rules, and cancellation stay in front of the
  provider: out-of-range speed is rejected before the hardware layer in every
  configuration; a disconnected session keeps the frozen silent software
  no-op (no hardware call), and `HardwareDroneController._speedPercent`
  continues to record the requested speed.

### Speed application (active)

```text
PUT /api/drone/speed { speed: s }                 (unchanged wire)
 → range check (controller → service → safety → provider, each layer)
 → DroneSafetyController: state gate Safe/CommandActive + bounded timeout
 → HardwareDroneController: session rules, records requested speed on success
 → RaspberryDroneHardware: range check → SpeedController.Apply(s)
 → SpeedController: duty(s) → configure ×2 → duty ×2 (PWM1 then PWM2)
 → dry-run: 4 records, all suppressed | real: never reached (startup abort)
 → success returns only after all four sink operations completed
```

- Speed success is a **software result** (`Applied`), never a physical
  acknowledgement, in every mode.
- A sink failure mid-sequence propagates: `SpeedController` updates nothing,
  the provider surfaces the failure, and the Task 26 safety path performs its
  existing reaction (STOP attempt → `Safe`/`Faulted` → rethrow). Task 30 adds
  no new transitions.

### Speed zero

- An explicit `speed = 0` request is a real application: both identifiers get
  `configure` + `write duty-percent=0` (four records in dry-run), not a
  skipped call. `LastRequestedSpeedPercent = 0`,
  `LastAppliedDutyPercent = 0`.
- What duty `0` does physically (motors at rest, coast, brake) depends on the
  unverified `ENA`/`ENB` wiring and is **not claimed** — `NOT VERIFIED`.

### Shutdown behavior (defined here; backlog requirement)

1. **Explicit speed 0** — as above, duty `0` issued through the sink.
2. **Graceful host shutdown** — `SpeedController.Dispose()` issues a
   best-effort duty-0 sequence (configure both + `write duty-percent=0`) if
   and only if a non-zero duty was ever applied; if the last applied duty was
   already `0` or nothing was applied, disposal issues nothing. Failures
   during disposal **propagate out of `Dispose`** (surfaced through the
   host's disposal logging) — never swallowed into a false success, never
   logged as physical. In dry-run the sequence is recorded and suppressed
   like any other operation.
3. **Session disconnect** — unchanged frozen contract: the controller forgets
   state and records speed 0 **without** calling the hardware layer. The
   consequence that a physical output would retain its last duty until
   disposal/power-off is documented here as an open physical-safety item for
   the future real-actuation/hardware-failsafe work — Task 30 does not change
   disconnect semantics (existing specs and tests freeze them).
4. **Crash / power loss** — no software guarantee is possible or claimed.
   Hardware-level fail-safe remains out of scope and `NOT VERIFIED`.

### Dry-run posture (logs)

`Program.cs` dry-run posture warnings are corrected so they never claim
"speed remains unsupported" when `SpeedMappingVerified=true` is active, and
never imply physical output when it is not (enumerated under Validation).
Real-mode posture text ("actuation not implemented yet") remains true — real
PWM actuation is still unimplemented by design.

### Configuration assertion semantics

Same as Task 29: `SpeedMappingVerified=true` is an **operator assertion, not
proof**. The evidence procedure (mirroring the Task 29 record, per user
decision) requires the `PWM speed envelope (Task 30)` record in
`docs/hardware/WIRING.md` to exist before the flag is set in any environment
beyond tests:

- all rows start `TBD` / `NOT VERIFIED`;
- asserted values are recorded only by the operator's own assertion, never
  generated by the task, spec, or shipped configuration (respecting
  `motor-wiring.md`'s no-invention rule);
- a disproven record is downgraded back to `NOT VERIFIED` with a note, and the
  flag must be reset;
- nothing in software reads `WIRING.md`.

## Interfaces

- **Configuration**: new section `PwmMapping` (shape above); `GPIO` section
  gains consumption of its existing `PWM1`/`PWM2` keys by the Task 30 binding
  only (`GpioPinConfiguration` itself stays digital-only; its "unconsumed
  (Task 30)" comment is corrected to point at the `PwmMapping` binding).
- **Application seam**: `IDroneHardware.ApplySpeedAsync` — unchanged
  signature and semantics.
- **HTTP/wire**: unchanged. No new status field, no `appliedSpeed` on the
  wire, no new endpoints.
- **Infrastructure seams**: `IPwmController`, `IRaspberryPwmPlatform` (shape
  above).
- **DI registrations** (composition root only): `PwmMapping`,
  `IPwmController` → `DryRunPwmController`, `SpeedController`, and
  `IDroneHardware` via factory. The unsafe-composition guard additionally
  rejects any dry-run graph containing `IRaspberryPwmPlatform` or
  `RaspberryPwmController`.
- **Evidence document**: `docs/hardware/WIRING.md` gains the `PWM speed
  envelope (Task 30)` section and is cross-referenced from this spec.

## Validation

Whenever the `PwmMapping` section exists (hardware-backed modes; mock skips
reading it):

1. `FrequencyHz` present and a positive integer — no default is supplied.
2. `MinDutyPercent` present, integer `0`–`100`.
3. `MaxDutyPercent` present, integer `0`–`100`, `>= MinDutyPercent`.
4. `SpeedMappingVerified` optional boolean, default `false`; a non-boolean
   value is an error, not a coercion.
5. Unknown keys in the section are errors (same rule as `MotorMapping`).
6. A valid `GPIO` section is required (existing explicit abort message);
   within it `PWM1`/`PWM2` must be present, positive integers, distinct from
   each other and from `Pin1`–`Pin4`.
7. **Aggregation**: every violation above (section content *and* PWM
   identifier problems) is collected into a single explicit startup failure
   listing all violations, raised **before** any service registration. The
   flag does not bypass validation: invalid content aborts even when
   `SpeedMappingVerified=false`.
8. Mode gates: asserted + `real` → startup abort (missing concrete
   platform); asserted + `dry-run` → active; `mock` → ignored.

Example shape of the aggregated failure (values are test fixtures, labeled as
such — never shipped):

```text
PwmMapping section is invalid:
 - FrequencyHz must be a positive integer (found "0");
 - MinDutyPercent must be between 0 and 100 (found "150");
 - MaxDutyPercent must be greater than or equal to MinDutyPercent;
 - "UpdateInterval" is not a recognized PwmMapping key;
 - GPIO:PWM2 must be a positive integer distinct from PWM1 and Pin1-Pin4.
```

## Error Cases

| Case | Observable behavior |
|---|---|
| `PwmMapping` content invalid (any mode that reads it) | `HostAbortedException` at startup with the full error list; nothing registered |
| Asserted `PwmMapping` under `real` | `HostAbortedException` naming the deferred concrete `IRaspberryPwmPlatform` |
| Speed out of range (`<0`, `>100`) | `ArgumentOutOfRangeException` at every layer, before any sink operation, in all modes and configurations; never clamped |
| Speed while capability inactive | `DroneUnavailableException` (default message when fully inert; capability-specific message when the provider is `Available`) |
| Speed while disconnected | Frozen silent software no-op (no hardware call) — unchanged |
| Speed while safety state not `Safe`/`CommandActive` | `DroneUnavailableException` from Task 26 — unchanged |
| Sink failure mid-application | Exception propagates; tracking untouched; Task 26 reaction (STOP attempt → `Safe`/`Faulted`) — unchanged transitions |
| Record-callback failure in dry-run | Propagates (fail-closed); never falls through to a physical sink |
| Unknown identifier / bad duty / bad frequency / write-before-configure at the sink | Rejected record + exception, mirroring the GPIO boundary contract |
| Cancellation before application | Operation cancels before any sink operation |
| Disposal failure | Propagates out of `Dispose` (host disposal logging); never converted into success |
| Any physical claim | Forbidden: success is software-level only; records are intent, not confirmation |

## Platform Requirements

- `net10.0`, Infrastructure-only placement; **no new package references**
  (no PWM library — the concrete platform is deferred precisely so no
  dependency is added speculatively).
- No Linux/PWM system calls, device paths, chip names, or library types in
  any shipped code; the real adapter is proven only against an in-memory fake.
- No compose, container, device-mapping, or privilege changes; the ARM64
  image build must remain green.
- Culture-invariant formatting for every record/parameter string.

## Security / Safety

- **No invented electrical values.** Frequency and duty bounds exist only as
  operator-supplied configuration; the shipped application configuration
  contains no `PwmMapping` section; no spec example, source comment, or test
  fixture presented as default may contain a concrete frequency/duty value
  (fixtures are labeled synthetic).
- **Assertion ≠ proof** (D7 discipline): `SpeedMappingVerified` gates
  software activation only; logs and posture warnings must state that the
  envelope is operator-asserted and not physically verified.
- **Dry-run structural suppression**: the dry-run graph contains the
  suppressing sink only; the unsafe-composition guard rejects real PWM
  platform registrations in dry-run.
- **Real stays gated**: D7 default `false` plus asserted-mapping abort —
  physical actuation remains impossible until the platform, wiring, and
  envelope prerequisites are verified by a later task.
- **No silent success**: speed never reports success without completing the
  sink sequence; failures are never swallowed; suppressed records are never
  worded as confirmation/acknowledgement.
- **Vocabulary isolation**: `PWM`, `duty`, `FrequencyHz`, identifiers, and
  platform types never appear in Domain, Application, API controllers, or the
  frontend.
- **Safety layer untouched**: Task 26 transitions, STOP path, and timeouts
  are reused exactly; speed failure behaves like any other operational
  failure.

## Verification Boundary

PC-verifiable in this task (claimed): configuration binding and aggregation,
duty computation, application sequence and ordering, tracking, dry-run record
shape and suppression, capability/activation matrices, safety-path
integration, shutdown bookkeeping, absence of physical sinks in dry-run,
vocabulary purity.

Explicitly **not** verified and not claimed: that the duty sequence reaches
any PWM hardware; that `PWM1`/`PWM2` reach `ENA`/`ENB`; that duty `0` stops
the motors; any frequency/duty value being electrically safe; PWM behavior on
a real Raspberry Pi; behavior under `real` (which aborts at startup by
design). These remain prerequisites recorded in the ledger and
`docs/hardware/WIRING.md` (`NOT VERIFIED`).

## Testing Scenarios

### Development-PC tests (`mock-tested` / `dry-run-tested`)

`DroneControl.Application.Tests`:

1. **`PwmMappingTests`** — valid section binds all fields and identifiers;
   flag `false` returns null only after full validation; flag `false` +
   invalid content still throws; every validation rule above (missing,
   non-integer, out-of-range, `Min > Max`, unknown key, malformed flag,
   missing/non-positive/duplicate/colliding `PWM1`/`PWM2`); all violations
   appear in one aggregated message; error messages contain no concrete
   frequency/duty values.
2. **`SpeedControllerTests`** — duty formula boundaries (`0 → 0`,
   `1 → Min`, `100 → Max`, truncation case, `Min = Max`, `Min = Max = 0`);
   deterministic `PWM1 → PWM2` order; configure-before-write ordering (4 ops
   per apply); out-of-range speeds rejected with zero sink calls;
   cancellation before apply; sink failure on any op propagates with tracking
   unchanged; tracking updates only after complete success and across
   repeated applies; disposal issues the duty-0 sequence iff a non-zero duty
   was applied (and nothing otherwise); disposal failure propagates.
3. **`DryRunPwmControllerTests`** — record fields (category `Pwm`,
   identifier, parameter formats, sequence, `DryRun=true`,
   `PhysicallyApplied=false`, suppression reason); rejected-operation records
   (unknown identifier, `frequencyHz <= 0`, duty out of `0`–`100`,
   write-before-configure) with rejection reason + matching exception types;
   `ObjectDisposedException`; record-callback failure fails closed.
4. **`RaspberryPwmControllerTests`** (fake platform) — identifier/frequency/
   duty forwarded unchanged; validation rejections before the platform;
   platform failures propagate; disposal contract.

`DroneControl.Api.Tests` (new `PwmMappingCompositionTests`, mirroring
`MotorMappingCompositionTests`):

5. Activation matrix rows: absent / valid-unasserted (no registrations, inert
   speed, zero sink records) / valid-asserted dry-run (active, `Available`,
   speed produces exactly the 4-record suppressed sequence per apply) /
   invalid (aggregated abort before registration) / asserted + `real`
   (abort naming `IRaspberryPwmPlatform`) / unasserted + `real` (inert) /
   mock (section never read — registrations identical with and without it).
6. Combined matrix with `MotorMapping`: both active; direction-only (commands
   work, specific speed message); PWM-only (`Available`, speed works,
   specific direction message); neither (default messages).
7. GPIO-section-required rule when `PwmMapping` exists; unsafe-composition
   guard rejects `IRaspberryPwmPlatform`/`RaspberryPwmController` in dry-run.
8. Full stack through `DroneSafetyController`: connect → speed applies,
   state `Speed` equals the requested value; record-callback failure produces
   the existing Task 26 transitions.
9. Shipped-config tests: `appsettings.json` contains no `PwmMapping` section;
   D7 default remains `false`; compose output unchanged (no new keys).
10. Existing `Program`/selection tests unchanged (mode parsing untouched).

### Regression

- All 219 existing backend tests pass with **exactly three** updated message
  assertion sites (enumerated below); no assertion of type, transition,
  ordering, or "no physical operation" behavior is weakened or removed.
- Frontend suite (121) passes untouched — no frontend file changes.
- Two consecutive `dotnet test` runs are identical; build is 0 errors /
  0 warnings; the `linux/arm64` image build stays green.

### Physical confirmation (deferred — not a mandatory AC for Task 30)

Bench observation of the asserted envelope (per the `WIRING.md` checklist:
record values → confirm `ENA`/`ENB` rows → observe speed `0` / low / `100`
response → record evidence/date) before `SpeedMappingVerified` may be set on
real hardware plans; Raspberry Pi runtime behavior; any real-mode actuation.
All `NOT VERIFIED` at task completion.

### Not proven by Task 30

Physical motor response to any duty or frequency; `ENA`/`ENB` control;
electrical safety of any envelope; real-mode PWM operation; abrupt
power/process-loss behavior; `appliedSpeed` beyond Infrastructure inspection
(no wire exposure, by decision).

## Acceptance Criteria

- [x] `dotnet build backend/DroneControl.sln` succeeds with 0 errors and 0
      warnings, and no package reference was added to any backend project.
- [x] A `PwmMapping` section binds `FrequencyHz` (integer `> 0`),
      `MinDutyPercent`/`MaxDutyPercent` (integers `0`–`100`, min ≤ max),
      `SpeedMappingVerified` (boolean, default `false`), and the `PWM1`/`PWM2`
      identifiers from the `GPIO` section; with the section present, any
      violation (including an invalid `PWM1`/`PWM2`: missing, non-integer,
      non-positive, duplicate, or colliding with `Pin1`–`Pin4`) aborts startup
      via `HostAbortedException` listing every violation, before any service
      registration — including when the flag is `false`.
- [x] Activation matrix holds observably: absent or unasserted section → no
      `PwmMapping`/`SpeedController`/`IPwmController` registrations, speed
      throws `DroneUnavailableException`, and zero PWM operations reach any
      sink; asserted section in `dry-run` → startup succeeds, provider
      reports `Available`, and each speed application produces exactly four
      suppressed records (`configure frequency-hz=…` ×2 then
      `write duty-percent=…` ×2, `PWM1` before `PWM2`) with category `Pwm`,
      `DryRun=true`, `PhysicallyApplied=false`; asserted section in `real` →
      startup aborts naming the deferred concrete `IRaspberryPwmPlatform`.
- [x] `mock` mode is unaffected: registrations and simulated speed behavior
      are identical with and without a `PwmMapping` section present.
- [x] Duty mapping matches the specification exactly: speed `0` → duty `0`;
      speed `1..100` → `Min + (Max − Min) × speed / 100` with integer
      truncation; speed `100` → `Max`; both identifiers always applied; a
      speed-`0` request performs the real duty-`0` sequence (not a skipped
      call).
- [x] Speed range invariant: values `<0` or `>100` throw
      `ArgumentOutOfRangeException` before any sink operation in every mode
      and activation configuration; nothing is ever clamped; the HTTP/wire
      contract is unchanged (single requested `speed` field).
- [x] Fail-closed application: a sink exception at any operation of the
      sequence propagates, leaves `LastRequestedSpeedPercent`/
      `LastAppliedDutyPercent` at their previous values, and drives no
      further sink operation; success updates tracking only after all four
      operations complete.
- [x] Capability matrix behaves as specified: fully inert → established
      default `DroneUnavailableException` message verbatim; provider
      `Available` with a missing capability → same exception type with a
      message naming the missing capability and the section/flag required;
      direction-only and speed-only combinations each work for their present
      capability.
- [x] Shutdown behavior: graceful disposal of an active `SpeedController`
      with a previously applied non-zero duty issues the duty-`0` sequence
      (asserted by dry-run records), issues nothing when no non-zero duty was
      applied, and propagates disposal failures; disconnect behavior is
      unchanged (no hardware call); crash/power-loss guarantees are
      documented as out of scope.
- [x] Dry-run integrity: the unsafe-composition guard aborts any dry-run
      graph containing `IRaspberryPwmPlatform` or `RaspberryPwmController`;
      no dry-run test or graph contains a physical PWM sink.
- [x] Exactly three pre-existing message-assertion sites are updated for
      post-Task-30 accuracy — `MotorMappingTests` (`UnknownIdentifier`
      fragment and active-provider speed fragment) and
      `MotorMappingCompositionTests` (PWM-identifier fragment) — plus the
      enumerated stale messages/comments they cover
      (`MotorMapping` PWM-rejection message, `RaspberryDroneHardware` speed
      reason + XML comment, `Program.cs` dry-run speed posture, the
      `DryRunOperationRecord`/`GpioPinConfiguration` reservation comments,
      and comments in the directly touched files); no behavior, type, or
      ordering assertion is weakened, and all 219 backend tests pass.
- [x] Tracking stays Infrastructure-only: no new field appears in
      `DroneState`, `DroneStatus`, any HTTP response, or the frontend; the
      frontend test suite (121) passes without any frontend change.
- [x] Shipped configuration and deployment are untouched: `appsettings.json`
      contains no `PwmMapping` section (no frequency/duty value ships
      anywhere), compose configuration output is unchanged, no device or
      privilege changes exist, and the D7 gate default remains `false`.
- [x] `docs/hardware/WIRING.md` gains the `PWM speed envelope (Task 30)`
      section with every row `TBD`/`NOT VERIFIED`, the assertion-before-flag
      procedure, a bench checklist, and cross-references; this specification
      lists `WIRING.md` as a dependency; `MEMORY.md` and
      `docs/ARCHITECTURE.md` are updated with the implemented/gated status
      without inventing any electrical value.
- [x] Validation runs green twice: `dotnet restore`/`dotnet build`
      (0 errors, 0 warnings), `dotnet test` twice with identical results
      (219 existing + new tests, none removed or weakened), `npm test`
      (121), and the `linux/arm64` image build succeeds.
