# Raspberry Pi ↔ L298N ↔ Motor Wiring — Task 28

Source specification: [`specs/hardware/motor-wiring.md`](../../specs/hardware/motor-wiring.md).

This is the single source of truth for the physical wiring between the
Raspberry Pi, the L298N motor driver, and the motors. It records what is
physically connected when that is known and marks everything else explicitly
`NOT VERIFIED`. No entry in this document is derived from pin numbers,
sequence, or example wiring. Nothing in software reads this file; it informs
specifications (Task 29, Task 30), not runtime behavior.

## Safety-critical unknowns

Read this before touching hardware:

- The Pi GPIO → L298N mapping (`IN1`–`IN4`, `ENA`, `ENB`) is
  **NOT VERIFIED** — no pin is known to reach any L298N terminal.
- The STOP electrical state is **NOT VERIFIED** and undecided: hardware STOP
  semantics are currently undefined. Downstream tasks and operators may not
  assume any STOP level.
- Motor terminal orientation and the resulting motion directions are
  **NOT VERIFIED**.
- Power and common-ground wiring are **NOT VERIFIED**.

This document is not authorization to energize hardware. The Task 26/27
software gates (including the `HARDWARE_MODE=real` external-failure-protection
gate) remain in force regardless of this document's existence.

## Status vocabulary

Every field below carries exactly one status:

- `CONFIRMED` — the connection was physically established by the operator
  (inspection, photo reference, or measurement actually performed). The
  evidence note and date are part of the record; a status without evidence
  does not count as `CONFIRMED`.
- `NOT VERIFIED` — not yet established from physical evidence. The field is
  present with a `TBD` marker so the document cannot be mistaken for
  complete.

There is no third status. "Probably", "standard wiring", or "assumed" are
not statuses.

## Given configuration (recorded, not derived)

```json
{
  "GPIO": {
    "Pin1": 23,
    "Pin2": 24,
    "Pin3": 21,
    "Pin4": 20,
    "PWM1": 12,
    "PWM2": 13
  }
}
```

This block names which pins the software may use. It assigns no motor
meaning and does not state which pin reaches which L298N terminal. Values
match `backend/src/DroneControl.Api/appsettings.json`, `AGENTS.md`, and
`docs/PRD.md` (consistency checked at documentation review). Whether
`PWM1`/`PWM2` connect to `ENA`/`ENB` at all is itself a wiring fact recorded
below — not an assumption.

## Wiring records

### Pi GPIO → L298N input mapping

| L298N terminal | Connected Pi pin (BCM number) | Status | Evidence + date |
| --- | --- | --- | --- |
| `IN1` | TBD | NOT VERIFIED | — |
| `IN2` | TBD | NOT VERIFIED | — |
| `IN3` | TBD | NOT VERIFIED | — |
| `IN4` | TBD | NOT VERIFIED | — |
| `ENA` control (jumper / external drive / driven by `PWM1`) | TBD | NOT VERIFIED | — |
| `ENB` control (jumper / external drive / driven by `PWM2`) | TBD | NOT VERIFIED | — |

No row may be filled by ordering (e.g. first pin ⇒ first input). A row
becomes `CONFIRMED` only with operator-confirmed physical observation; the
observation is the evidence, never the pattern.

### Motors

| Field | Value | Status | Evidence + date |
| --- | --- | --- | --- |
| Motor A terminals (L298N output side) | TBD | NOT VERIFIED | — |
| Motor B terminals (L298N output side) | TBD | NOT VERIFIED | — |
| Motor terminal orientation (physical position/label, direction-neutral) | TBD | NOT VERIFIED | — |

### Power and ground

| Field | Value | Status | Evidence + date |
| --- | --- | --- | --- |
| Power wiring (what supply connects where, as observed) | TBD | NOT VERIFIED | — |
| Ground / common ground between Pi, L298N, and supply (as observed) | TBD | NOT VERIFIED | — |

No voltage, current, or other electrical limit is recorded here; none has
been read from physical device markings or operator-supplied references.

### STOP electrical state

| Field | Value | Status | Evidence + date |
| --- | --- | --- | --- |
| Chosen STOP electrical state (intended output condition) | TBD — undecided | NOT VERIFIED | — |

Until this row is `CONFIRMED`, hardware STOP semantics remain undefined
(see Safety-critical unknowns).

## Direction mapping (Task 29)

Source specification:
[`specs/hardware/motor-control.md`](../../specs/hardware/motor-control.md).

Runtime direction data lives ONLY in the `MotorMapping` configuration
(`backend/src/DroneControl.Api/appsettings.json` or environment variables) —
never in this file, never in source-code defaults. This table records the
operator assertion and its physical observation. Every row starts
`NOT VERIFIED`. Nothing in software reads this file; the record is the
evidence the operating procedure requires BEFORE setting
`MotorMapping:DirectionMappingVerified=true`, which is an operator assertion,
not proof.

| Semantic command | Asserted `MotorMapping` values | Observed physical result | Status | Evidence + date |
| --- | --- | --- | --- | --- |
| `forward` | TBD — no mapping asserted | TBD | NOT VERIFIED | — |
| `backward` | TBD — no mapping asserted | TBD | NOT VERIFIED | — |
| `left` | TBD — no mapping asserted | TBD | NOT VERIFIED | — |
| `right` | TBD — no mapping asserted | TBD | NOT VERIFIED | — |
| `stop` | TBD — no mapping asserted | TBD (STOP electrical state undecided, above) | NOT VERIFIED | — |

Rules:

1. "Asserted `MotorMapping` values" must match the deployed configuration
   exactly (the level for each of `Pin1`–`Pin4` per command). A mismatch
   between this record and the deployed configuration is an Open issue and is
   reported, never silently reconciled.
2. A row becomes `CONFIRMED` only when that command's physical result was
   actually observed on the bench, with evidence and date — reusing the
   status vocabulary and downgrade-on-disproof rule of this document.
3. While every row is `NOT VERIFIED`, do NOT set
   `MotorMapping:DirectionMappingVerified=true`. The flag asserts that these
   observations exist; it can never substitute for them.

### Direction observation checklist (complete on the physical hardware)

For each command: assert the mapping only in a context the operator has
independently confirmed safe (this document prescribes no electrical
procedure), observe the actual result, capture evidence, add the date, then
change the row's status.

- [ ] `forward` — observed result: ______ evidence/date: ______
- [ ] `backward` — observed result: ______ evidence/date: ______
- [ ] `left` — observed result: ______ evidence/date: ______
- [ ] `right` — observed result: ______ evidence/date: ______
- [ ] `stop` — observed result as energized: ______ evidence/date: ______
- [ ] Rows above updated and `MotorMapping:DirectionMappingVerified` set
      only after all five observations exist: ______

## PWM speed envelope (Task 30)

Source specification:
[`specs/hardware/pwm.md`](../../specs/hardware/pwm.md).

Evidence record for the `PwmMapping:SpeedMappingVerified` operator
assertion. Software never reads this file; the flag gates software
activation only (assertion ≠ proof). **No row below contains a value
generated by any task, specification, or shipped configuration** — asserted
values are recorded only by the operator's own bench verification. The
shipped default configuration has NO `PwmMapping` section at all.

### Envelope rows

| Row | Value | Status |
|---|---|---|
| `PWM1` identifier — physical destination (does it reach `ENA`?) | TBD | NOT VERIFIED |
| `PWM2` identifier — physical destination (does it reach `ENB`?) | TBD | NOT VERIFIED |
| PWM frequency used on the bench (`FrequencyHz`) | TBD | NOT VERIFIED |
| Minimum usable duty for non-zero speeds (`MinDutyPercent`) | TBD | NOT VERIFIED |
| Maximum duty (`MaxDutyPercent`) | TBD | NOT VERIFIED |
| Duty `0` physical behavior (rest / coast / brake) | TBD | NOT VERIFIED |
| Motor response at low duty | TBD | NOT VERIFIED |
| Motor response at duty `100` | TBD | NOT VERIFIED |
| Electrical limits of the motors / L298N for any duty or frequency | TBD | NOT VERIFIED |

### Procedure (assertion before flag)

1. Keep every row `TBD` / `NOT VERIFIED` (the current state).
2. Perform the bench checklist below in a context the operator has
   independently confirmed safe (this document prescribes no electrical
   procedure) and record the observed values/answers in the rows — by hand,
   as operator assertion.
3. Only after the rows are recorded, set
   `PwmMapping:SpeedMappingVerified=true` in the target environment's
   configuration.
4. If any recorded row is later disproven, downgrade it to `NOT VERIFIED`
   with a note AND reset the flag to `false`.
5. Nothing in software reads this file; no value here reaches runtime
   unless an operator transcribes it into configuration.

### Bench checklist (complete on the physical hardware)

- [ ] `PWM1`/`PWM2` → `ENA`/`ENB` continuity observed: ______ evidence/date: ______
- [ ] Chosen frequency recorded and observed on the bench: ______ evidence/date: ______
- [ ] Speed `0` (duty `0`) observed motor behavior: ______ evidence/date: ______
- [ ] Low duty observed motor response: ______ evidence/date: ______
- [ ] Duty `100` observed motor response: ______ evidence/date: ______
- [ ] Rows above updated and `PwmMapping:SpeedMappingVerified` set only
      after all observations exist: ______

Recorded evidence so far: **none — every row remains `NOT VERIFIED` at
Task 30 completion.**

### Cross-references

- PWM specification: [`specs/hardware/pwm.md`](../../specs/hardware/pwm.md) (Task 30 — depends on this record)
- Direction mapping record: `Direction mapping (Task 29)` section above
- Prerequisites ledger (PWM subsystem selection + safe duty envelope rows
  remain open): [`specs/architecture/runtime-deployment.md`](../../specs/architecture/runtime-deployment.md)
- Physical wiring unknowns: `Safety-critical unknowns` and `Confirmation
  checklist` sections of this document

## Confirmation checklist (complete on the physical hardware)

For each item: record the observed connection, capture evidence (inspection
note / photo reference / measurement actually performed), add the date, then
change that row's status from `NOT VERIFIED` to `CONFIRMED`.

- [ ] `IN1` ← observed Pi pin: ______  evidence/date: ______
- [ ] `IN2` ← observed Pi pin: ______  evidence/date: ______
- [ ] `IN3` ← observed Pi pin: ______  evidence/date: ______
- [ ] `IN4` ← observed Pi pin: ______  evidence/date: ______
- [ ] `ENA` control as physically present: ______  evidence/date: ______
- [ ] `ENB` control as physically present: ______  evidence/date: ______
- [ ] Motor A terminals: ______  evidence/date: ______
- [ ] Motor B terminals: ______  evidence/date: ______
- [ ] Motor terminal orientation (physical labels/positions): ______  evidence/date: ______
- [ ] Power wiring as observed: ______  evidence/date: ______
- [ ] Ground / common ground as observed: ______  evidence/date: ______
- [ ] Chosen STOP electrical state: ______  evidence/date: ______

## Update rules

1. A status changes only when new physical evidence arrives; record the
   source and date in the corresponding row.
2. If evidence disproves a `CONFIRMED` field, downgrade it to `NOT VERIFIED`
   with a note explaining the correction.
3. If this document ever conflicts with the GPIO configuration, record the
   conflict under Open issues and report it. Neither source is edited
   silently to make them agree.

## Open issues

None recorded. (Conflicts between documented wiring and the GPIO
configuration cannot be evaluated while every mapping field is `TBD`.)

## Cross-references

- Source specification: [`specs/hardware/motor-wiring.md`](../../specs/hardware/motor-wiring.md)
- Direction mapping specification: [`specs/hardware/motor-control.md`](../../specs/hardware/motor-control.md)
- PWM speed envelope specification: [`specs/hardware/pwm.md`](../../specs/hardware/pwm.md)
- Prerequisites ledger: [`specs/architecture/runtime-deployment.md`](../../specs/architecture/runtime-deployment.md)
- Device/permission inventory (separate concern; does not verify motor
  wiring): [`docs/hardware/raspberry-pi-inventory.md`](raspberry-pi-inventory.md)
- GPIO configuration values: `backend/src/DroneControl.Api/appsettings.json`

## Verification status

```text
Documentation review (completeness, status discipline, GPIO consistency):
    performed on this document — PC, documentation level only
Physical wiring evidence: NOT VERIFIED
Motor direction / STOP behavior / electrical properties: NOT VERIFIED
PWM speed envelope (frequency / duty / ENA-ENB control): NOT VERIFIED
```

Document review proves documentation quality only. It cannot prove physical
wiring.
