# Motor Direction Mapping (MotorController)

## Purpose

Define the Infrastructure `MotorController` that maps semantic `DroneCommand`
values to explicit digital output operations on the configured GPIO
identifiers, and the validated `MotorMapping` configuration that feeds it.

The central safety property: an unverified mapping never actuates. The
mapping is data supplied by the operator, activated only by an explicit
operator assertion after bench observation. Until that assertion exists, the
Raspberry provider behaves exactly as it does today (inert, honest-
unavailable) — honoring the backlog rule "Do not implement unverified motor
mappings". The task also adds the direction-observation record to
`docs/hardware/WIRING.md`.

Operator evidence state at planning time (confirmed with the operator): only
the GPIO identifier configuration is known. No wiring row of `WIRING.md` is
`CONFIRMED`, no physical direction has been observed, and the STOP electrical
state is undecided. Therefore every concrete HIGH/LOW combination in this
task is operator-supplied data at implementation time — never a value
invented by this specification, the code, or its tests.

## Dependencies

- `specs/hardware/motor-wiring.md` — `WIRING.md` is the evidence source for
  wiring and direction; its status vocabulary (`CONFIRMED` with evidence vs
  `NOT VERIFIED`) and update rules are reused unchanged.
- `specs/architecture/runtime-deployment.md` — prerequisites ledger: "Motor
  direction mapping (only once Task 28 confirms wiring) → Task 29"; D3/D5
  (composition-root selection, fail-fast, no silent fallback).
- `specs/hardware/hardware-abstraction.md` — `IDroneHardware` contract:
  `Applied` is software-level (never physical confirmation), unavailable →
  `DroneUnavailableException`, no new exception types, `Rejected` is for
  logical refusals by a working layer only.
- `specs/hardware/raspberry-hardware-provider.md` — the provider's inert
  internals are explicitly replaced "layer by layer" by Tasks 24–30 without
  touching anything above the seam.
- `specs/hardware/gpio.md` — `IGpioController`, `GpioPinConfiguration`
  (identifiers only, no semantics), `GpioPinValue` (High/Low transport
  values with no electrical meaning).
- `specs/hardware/dry-run.md` — real and dry-run share the same upstream
  operation flow; only the final sink differs. `MotorController` must be
  mode-agnostic.
- `specs/hardware/failsafe.md` — Task 26 owns timeout/STOP/recovery policy;
  operation failures follow its transitions. The D7 real-mode gate is
  independent and untouched.
- `specs/backend/domain-model.md` — `DroneCommand` (five semantic values,
  `Stop` included) reused unchanged.

## Scope

- Infrastructure `MotorController`: applies a validated mapping through
  `IGpioController`, consumed by `RaspberryDroneHardware` (below the
  `IDroneHardware` seam).
- `MotorMapping` configuration: a `DirectionMappingVerified` operator
  assertion (default `false`) plus a per-command level table covering every
  configured digital identifier with explicit `HIGH`/`LOW` values, including
  an explicit `stop` entry.
- Composition-root behavior for hardware-backed modes (`dry-run`, `real`):
  activation matrix below. Mock mode never registers any of it.
- Provider behavior when active: `HardwareAvailability.Available`
  (software-level layer presence), commands routed through `MotorController`,
  `Applied` returned only after all output operations complete.
- Startup validation of `MotorMapping` (fail-fast, explicit error list) when
  the section is present.
- Documentation: a "Direction mapping (Task 29)" record appended to
  `docs/hardware/WIRING.md` (command → asserted values → observed physical
  result → status), all rows `NOT VERIFIED` with the current evidence, plus a
  bench observation checklist.
- Durable status updates (`MEMORY.md`, `docs/ARCHITECTURE.md`) as part of
  implementation.

## Out of Scope

- Supplying or confirming physical wiring, direction observations, or the
  STOP electrical state — bench evidence is the operator's; this
  specification never provides a concrete HIGH/LOW combination.
- PWM, duty cycles, frequencies, and speed behavior (Task 30):
  `ApplySpeedAsync` stays explicitly unsupported while the mapping is
  active.
- Any change to `DroneCommand`, `IDroneController`, `IDroneHardware`,
  `CommandExecutionResult`, HTTP endpoints, response fields, or the
  frontend.
- Removing or weakening the Task 26 fail-safe policy, the D7 real-mode gate,
  dry-run suppression, or the mock graph.
- Electrical limits, coast/brake theory, device paths, permissions,
  Compose device mappings, camera work.
- Reading `WIRING.md` from software — the document informs humans and specs,
  never code.

## Architecture

```text
API → DroneSafetyController → HardwareDroneController
   → IDroneHardware (RaspberryDroneHardware)
        → MotorController            ← this task (Infrastructure)
             → IGpioController       (Task 25 seam)
                  ├── real sink      (concrete char-device ioctl GPIO platform — implemented)
                  └── dry-run sink   (Task 27: records + suppresses)
```

Rules:

- `MotorController` and `MotorMapping` live in Infrastructure only.
  Domain/Application/API/frontend gain no pin, level, or mapping knowledge.
- `MotorController` is mode-agnostic: it never reads `HARDWARE_MODE`. Mode
  selection stays at the composition root; only the sink differs
  (dry-run.md Interfaces rule).
- Only `RaspberryDroneHardware` internals change — explicitly permitted by
  the provider spec ("Tasks 24–30 replace the inert internals layer by
  layer"). Everything above the `IDroneHardware` seam is untouched.
- Mapping values reach the process only through the existing .NET
  configuration system (`appsettings.json` section and/or environment
  variables); no new configuration mechanism, file format, or dependency.

## Domain Model

No new Domain or Application types. Reused: `DroneCommand`,
`CommandExecutionResult`, `HardwareAvailability`, `GpioPinValue`,
`GpioPinConfiguration`, `IGpioController`.

New Infrastructure-only types (names indicative):

```text
MotorMapping
  DirectionMappingVerified : bool        // operator assertion, default false
  Commands : exactly the five DroneCommand names
             → per configured digital identifier (Pin1..Pin4): HIGH | LOW

MotorController
  Apply(DroneCommand)                    // validates, configures, writes
```

Mapping configuration shape (STRUCTURE ONLY — placeholders, no real values):

```text
MotorMapping
  DirectionMappingVerified = false
  Commands
    forward : { Pin1: <HIGH|LOW>, Pin2: <HIGH|LOW>, Pin3: <HIGH|LOW>, Pin4: <HIGH|LOW> }
    backward: { Pin1: <HIGH|LOW>, Pin2: <HIGH|LOW>, Pin3: <HIGH|LOW>, Pin4: <HIGH|LOW> }
    left    : { Pin1: <HIGH|LOW>, Pin2: <HIGH|LOW>, Pin3: <HIGH|LOW>, Pin4: <HIGH|LOW> }
    right   : { Pin1: <HIGH|LOW>, Pin2: <HIGH|LOW>, Pin3: <HIGH|LOW>, Pin4: <HIGH|LOW> }
    stop    : { Pin1: <HIGH|LOW>, Pin2: <HIGH|LOW>, Pin3: <HIGH|LOW>, Pin4: <HIGH|LOW> }
```

Validation rules (a mapping is valid only if all hold):

1. The section contains `DirectionMappingVerified` (bool, default `false`).
2. All five commands are present — no more, no fewer.
3. Every entry covers every digital identifier of the bound
   `GpioPinConfiguration` (Pin1..Pin4) — full coverage, so `stop` states an
   explicit hardware state for every line the mapping governs.
4. Every value token is exactly `HIGH` or `LOW` (case-insensitive, trimmed).
   PWM identifiers are not accepted (Task 30 owns PWM).
5. No unknown identifiers or commands.

The `HIGH`/`LOW` combinations themselves are operator data. This
specification, shipped defaults, and source code contain none.

## Behavior

### Activation matrix (hardware-backed modes only)

| Mapping section | Verified flag | Result |
| --- | --- | --- |
| absent | absent (false) | Inactive — provider behaves exactly as today: `Unavailable`, operations throw `DroneUnavailableException` → 503; zero `IGpioController` calls |
| present, valid | `false` | Inactive — identical legacy behavior (config is validated but unused) |
| present, valid | `true` | Active — provider reports `Available`, routes commands through `MotorController` |
| present, invalid | any | Startup abort with an explicit error list (`HostAbortedException`) — never a silent fallback to inert or mock |
| absent | `true` (e.g. env-only) | Startup abort — an assertion without mapping data is a contradiction |

Mock mode: the provider and mapping are never registered; the flag and
section are ignored; today's mock graph and tests are untouched.

### Command application (active)

1. Validate the command value (existing enum integrity; out-of-range →
   `ArgumentOutOfRangeException`).
2. Ensure the mapping's identifiers are configured as outputs
   (`ConfigureOutput`) before any write; writes go only to configured
   outputs.
3. Write the command's level per identifier in a deterministic, documented
   order.
4. Return `Applied` only after every output operation completed.
   `Applied` remains software-level: output operations were issued —
   never a claim that a motor moved or that hardware confirmed anything.
5. `stop` uses the mapping's `stop` entry like any other command; it
   travels the same Task 26 path (no special casing) and its hardware state
   exists only because validation requires the entry.

Failure: any output exception propagates unchanged (no new exception
types; existing error mapping applies). Operations are sequential — a
mid-sequence failure can leave earlier lines written; there is no atomicity
claim and no retry invented here (Task 26 owns STOP/retry policy). In
dry-run the same failure semantics apply through the suppressing sink
(record failure → fail-closed, already specified by Task 27).

### Inactive behavior

Identical to today's inert provider (the safe default): `Unavailable`,
`DroneUnavailableException`, the safety baseline in hardware modes faults at
startup as it does now, and nothing writes to any sink. While inactive no
output operation occurs — including for `stop` — which is safe precisely
because no movement is possible either (all-or-nothing activation).

### Speed

Unchanged and explicitly unsupported while active: `ApplySpeedAsync` fails
explicitly (never silently succeeds, never pretends speed was applied).
PWM behavior arrives with Task 30.

### Configuration assertion semantics

`DirectionMappingVerified=true` is an operator assertion, not proof — the
same stance as the D7 flag. The operating procedure requires the operator to
first complete the `WIRING.md` confirmation checklist and the direction
observation record; software cannot verify a document and does not pretend
to. In `real` mode two independent assertions are therefore required to do
anything physical: the mapping assertion here and Task 26's external-failure-
protection flag; both default `false`.

### Direction observation record (documentation)

`docs/hardware/WIRING.md` gains a "Direction mapping (Task 29)" section:

```text
| Semantic command | Asserted MotorMapping values | Observed physical result | Status | Evidence + date |
```

- With current evidence every row is `NOT VERIFIED` (observed result `TBD`).
- Status changes only with bench evidence, reusing `motor-wiring.md`'s
  vocabulary and downgrade-on-disproof rule.
- A bench observation checklist accompanies the table (one item per
  command), to be completed by the operator; this specification prescribes
  no electrical procedure.

## Interfaces

Unchanged public contracts: `IDroneController`, `IDroneHardware`,
`DroneCommand`, `CommandExecutionResult`, HTTP endpoints and JSON fields.
New types are Infrastructure-internal and reachable only from the
composition root. Optional deployment note: the assertion flag may be
exposed through the existing compose environment-passthrough pattern (as
`SAFETY__*` is); defaults must keep `docker compose config` output
unchanged for an unconfigured checkout.

## Validation

At implementation time:

- Startup: strict validation per the rules above when the section (or an
  env-provided flag) exists; abort messages enumerate every violated rule.
- Unit: mapping parse/validation matrix; `MotorController` write sequences
  against a recording `IGpioController` fake using a clearly labeled
  synthetic fixture mapping; zero-write proof for both inactive states;
  failure propagation proof.
- Composition: activation-matrix cases in Api.Tests (patterns from
  `DroneRuntimeSelectionTests`); mock graph contains no mapping types.
- Regression: `dotnet build`, `dotnet test` (no existing test removed or
  weakened), `npm test`; `docker compose config` unchanged by default.
- Content greps: no concrete HIGH/LOW combination asserted as real anywhere
  (spec, shipped defaults, source); no pin/level/mapping vocabulary added
  above Infrastructure; `DroneCommand` and wire contracts untouched.

## Error Cases

- Invalid mapping content with or without the flag → startup abort, explicit
  list, no silent inert/mock fallback (D5).
- Flag `true` without a mapping section → startup abort (contradiction).
- Command while inactive → existing `DroneUnavailableException` → 503.
- Sink failure mid-command → exception propagates; no `Applied`; Task 26
  treats it as an operational failure and applies its transition (fault/STOP
  attempt as already specified).
- Operator asserts the flag without physical confirmation → software cannot
  detect it; documented as a process violation, mirroring the D7 flag's
  "assertion is not proof" stance.
- Bench later disproves an asserted mapping → operator reverts the flag and
  downgrades the `WIRING.md` row (documented procedure; no automatic
  detection).
- Real mode with active mapping but D7 flag `false` → unchanged: no
  controller operations, safety stays Faulted.

## Platform Requirements

- Pure .NET/BCL code; no new NuGet packages; builds and tests run on the
  development PC; existing `linux/arm64` image build unaffected.
- No device nodes, privileges, or Compose device mappings are introduced or
  required by this task.

## Security / Safety

- Default state cannot actuate: absent section or `false` flag → zero
  output operations, ever.
- Activation is all-or-nothing and explicit; there is no mode in which a
  partially specified mapping runs.
- The flag is an assertion, not verification; the procedure (not the
  software) requires `WIRING.md` confirmation first.
- `real` mode remains double-gated (mapping assertion + D7); dry-run
  remains structurally unable to write physically; mock remains simulated.
- Software-level honesty preserved: `Applied`/`Connected` never become
  physical claims; no new wire fields; the frontend keeps labeling acks
  `simulated`.
- No pin, level, or mapping knowledge above Infrastructure.

## Testing Scenarios

### Development-PC tests (`mock-tested`)

1. Validation matrix: every invalid shape (missing `stop`, missing/extra
   command, incomplete pin coverage, bad level token, unknown identifier,
   PWM key, flag without section) → explicit startup failure; valid shapes →
   accepted.
2. Inactive (section absent; section valid + flag false): provider reports
   `Unavailable`, operations throw `DroneUnavailableException`, recording
   sink shows **zero** writes.
3. Active with a synthetic fixture mapping (labeled fake in the test): each
   of the five commands produces exactly the configured sequence
   (configure-before-write, deterministic order); `stop` uses the `stop`
   entry; `Applied` only after completion.
4. Sink throws mid-sequence → exception propagates, no `Applied`, safety
   path transition per Task 26 (faulted).
5. Dry-run integration: active fixture mapping in a dry-run graph → same
   upstream sequence, every record suppressed (`dryRun=true`,
   `physicallyApplied=false`); all Task 27 tests still pass.
6. Mock regression: mock graph has no mapping types; full suites pass with
   no existing test removed or weakened.
7. Content review: no real HIGH/LOW combination anywhere; no new vocabulary
   above Infrastructure.

These prove software mechanics only. They cannot prove that any combination
moves a motor in a particular direction.

### Physical confirmation (deferred — not a mandatory AC for Task 29)

On the bench: complete the `WIRING.md` confirmation checklist, observe each
command's actual physical result, record it in the direction table with
evidence, set `DirectionMappingVerified=true`, exercise dry-run first, then
(real-mode, after the D7 assertion) real. Only then may direction behavior
be reported `real-hardware-verified`.

### Not proven by Task 29

- That any asserted combination produces the intended physical direction.
- STOP electrical behavior on the physical build.
- PWM/speed response, electrical limits, or power-loss behavior.

## Acceptance Criteria

- [x] `dotnet build` succeeds with no new package dependencies, and the new
      types live only in Infrastructure (composition root is their only
      consumer).
- [x] Inactive proof: with the mapping section absent, and again with a
      valid section but flag `false`, the provider reports `Unavailable`,
      operations throw `DroneUnavailableException`, and a recording
      `IGpioController` fake shows zero operations.
- [x] Active proof (synthetic fixture, labeled as fake): each of the five
      commands applies exactly the configured levels to every configured
      digital identifier in deterministic order with configure-before-
      write; `stop` uses the mapping's `stop` entry; `Applied` is returned
      only after all operations complete.
- [x] Validation proof: each invalid mapping shape (including flag `true`
      with no section) aborts startup with an explicit message; the mock
      graph never registers mapping types.
- [x] Failure proof: a sink exception mid-command propagates (no
      `Applied`) and the Task 26 safety path responds per its existing
      transitions.
- [x] Dry-run proof: an active fixture mapping in the dry-run graph
      produces the same upstream sequence with every record suppressed;
      all Task 27 dry-run tests still pass.
- [x] Unchanged contracts: `DroneCommand`, `IDroneController`,
      `IDroneHardware`, HTTP endpoints/fields, and frontend files are
      untouched; every pre-existing test still passes unmodified.
- [x] Speed stays explicitly unsupported while active (test proves it never
      silently succeeds).
- [x] No concrete HIGH/LOW combination appears in the specification,
      shipped default configuration, or source as an asserted real value;
      the default shipped configuration contains no mapping section and
      flag `false`.
- [x] `docs/hardware/WIRING.md` gains the direction-mapping record with all
      rows `NOT VERIFIED` (current evidence), inherited evidence-based
      update rules, and a bench observation checklist.
- [x] `real` mode remains double-gated (mapping assertion + D7, both
      default `false`) and `docker compose config` output is unchanged for
      an unconfigured checkout with no devices or privileges added.
- [x] No pin/level/mapping vocabulary added in Domain, Application, API
      controllers, or frontend (grep-level check).
- [x] Durable status docs (`MEMORY.md`, `docs/ARCHITECTURE.md`) reflect the
      new layer and its verification level without copying mapping values.
