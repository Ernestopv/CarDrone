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

- The Pi GPIO → L298N mapping terminal *labels* (`IN1`–`IN4`) remain
  **NOT VERIFIED**, but the functional effect is now confirmed: `Pin1`–`Pin4`
  drive the motors (all five directions) and `ENA`/`ENB` are driven by
  `PWM1`/`PWM2` (BCM 12/13) — see Direction mapping and PWM speed envelope.
- The STOP electrical state is **CONFIRMED** as `Pin1`–`Pin4` all `LOW`
  (the operator observed `stop` stopping the wheels).
- Motor terminal orientation is **NOT VERIFIED**; the resulting motion
  directions are **CONFIRMED** for all five commands.
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
| `ENA` control (jumper / external drive / driven by `PWM1`) | Software-driven: `PWM1` (BCM 12) held HIGH from the host (no physical jumper) | CONFIRMED | Operator: motors energize only while GPIO12/13 are held HIGH via `gpioset`, and release on stop; 2026-10-05 |
| `ENB` control (jumper / external drive / driven by `PWM2`) | Software-driven: `PWM2` (BCM 13) held HIGH from the host (no physical jumper) | CONFIRMED | Same evidence as `ENA`; the `ENA`↔12 / `ENB`↔13 individual pairing is not separately distinguished; 2026-10-05 |

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
| Chosen STOP electrical state (intended output condition) | `Pin1`–`Pin4` all `LOW` (L298N inputs low) | CONFIRMED | Operator: `stop` command stops the wheels (asserted mapping all `LOW`); 2026-10-05 |

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
| `forward` | `Pin1=HIGH Pin2=LOW Pin3=HIGH Pin4=LOW` | Car moved forward | CONFIRMED | Operator observation: API `{"command":"forward"}`; 2026-10-05 |
| `backward` | `Pin1=LOW Pin2=HIGH Pin3=LOW Pin4=HIGH` | Car reversed | CONFIRMED | Operator observation: API `{"command":"backward"}`; 2026-10-05 |
| `left` | `Pin1=LOW Pin2=HIGH Pin3=HIGH Pin4=LOW` | Car turned left | CONFIRMED | Operator observation (watched the car): API `POST /api/drone/command {"command":"left"}`; 2026-10-05 |
| `right` | `Pin1=HIGH Pin2=LOW Pin3=LOW Pin4=HIGH` | Car turned right | CONFIRMED | Operator observation: API `{"command":"right"}`; 2026-10-05 |
| `stop` | `Pin1=LOW Pin2=LOW Pin3=LOW Pin4=LOW` | Wheels stopped | CONFIRMED | Operator observation: API `{"command":"stop"}`; 2026-10-05 |

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

- [x] `forward` — observed result: car advanced forward; evidence/date: operator observation, 2026-10-05
- [x] `backward` — observed result: car reversed; evidence/date: operator observation, 2026-10-05
- [x] `left` — observed result: car turned left; evidence/date: operator observation, 2026-10-05
- [x] `right` — observed result: car turned right; evidence/date: operator observation, 2026-10-05
- [x] `stop` — observed result as energized: wheels stopped; evidence/date: operator observation, 2026-10-05
- [x] Rows above updated and `MotorMapping:DirectionMappingVerified` set
      only after all five observations exist: all five observations now exist
      (2026-10-05); the flag was already set by the operator

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
| `PWM1` identifier — physical destination (does it reach `ENA`?) | Reaches the L298N enable: software PWM on `PWM1` (BCM 12) varies motor speed | CONFIRMED |
| `PWM2` identifier — physical destination (does it reach `ENB`?) | Reaches the L298N enable: software PWM on `PWM2` (BCM 13) varies motor speed | CONFIRMED |
| PWM frequency used on the bench (`FrequencyHz`) | `20000` Hz (motors ran; the audible driver whine is gone) | CONFIRMED |
| Minimum usable duty for non-zero speeds (`MinDutyPercent`) | ≈`55` % **under load** (measured: duty `48` % did not move the car, duty `56` % did); configured `MinDutyPercent=55` | CONFIRMED |
| Maximum duty (`MaxDutyPercent`) | `100` | CONFIRMED |
| Duty `0` physical behavior (rest / coast / brake) | `speed=0` sets duty `0`; the exact rest/coast/brake behavior was not characterized | NOT VERIFIED |
| Motor response at low duty | Duty `25` % moves (clearly slower than `60` / `100`) | CONFIRMED |
| Motor response at duty `100` | Duty `100` % moves (fastest observed) | CONFIRMED |
| Electrical limits of the motors / L298N for any duty or frequency | Not measured | NOT VERIFIED |

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

- [x] `PWM1`/`PWM2` → `ENA`/`ENB` continuity observed: motors respond to PWM duty on BCM 12/13; evidence/date: operator observation, 2026-10-05
- [x] Chosen frequency recorded and observed on the bench: `1000` Hz, motors ran; evidence/date: operator observation, 2026-10-05
- [ ] Speed `0` (duty `0`) observed motor behavior: not characterized — evidence/date: ______
- [x] Low duty observed motor response: duty `25` % moves; with `MinDutyPercent=20` the mapped low speeds `1` / `5` / `20` also move; evidence/date: operator observation, 2026-10-05
- [x] Duty `100` observed motor response: duty `100` % fastest; evidence/date: operator observation, 2026-10-05
- [x] Rows above updated and `PwmMapping:SpeedMappingVerified` set only
      after all observations exist: flag set in the target `pwm.env`; the
      duty-`0` behavior remains uncharacterized

Recorded evidence (2026-10-05, target): the operator-asserted envelope
(`FrequencyHz=20000`, `MinDutyPercent=55`, `MaxDutyPercent=100`) was exercised
through the app. Speeds produced clearly different, increasing motor speeds, and
at `20000` Hz the motors ran with the driver whine gone. The minimum-duty
threshold was re-measured **under load**: duty `48` % did **not** move the car
while duty `56` % did, so `MinDutyPercent` was raised from `20` to `55` — the
1–100 slider now maps to `55`–`100` % duty, so every non-zero speed moves.
(`PWM1`/`PWM2` → `ENA`/`ENB`, the frequency and the low/high duty responses are
`CONFIRMED`.) Still `NOT VERIFIED`: the exact duty-`0` rest/coast/brake behavior
and electrical limits.

Device-tree prerequisite recorded: by default the `pwm-2chan` overlay drives
GPIO18/19; reaching GPIO12/13 required
`dtoverlay=pwm-2chan,pin=12,func=4,pin2=13,func2=4` (Alt0) and disabling the
onboard analogue audio (`dtparam=audio=off`, it shares the PWM channels).

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

None recorded. (All five direction observations now exist, so the
`MotorMapping:DirectionMappingVerified=true` assertion is consistent with this
record. IN1–IN4 terminal labels, motor terminals and power/ground remain
`NOT VERIFIED` but are not conflicts.)

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
Physical wiring evidence: PARTIAL — ENA/ENB software drive, STOP state and the
    full direction mapping CONFIRMED by operator observation (2026-10-05);
    IN1–IN4 terminal mapping, motor terminals, power/ground still NOT VERIFIED
Motor direction: CONFIRMED for all five commands (forward/backward/left/right/stop)
STOP behavior: CONFIRMED (stop command stops the wheels)
PWM speed envelope: PARTIAL — PWM → ENA/ENB, frequency 20 kHz, duty 25/100
    responses and the ~55 % under-load minimum-duty threshold CONFIRMED; duty-0
    behavior and electrical limits NOT VERIFIED
```

Document review proves documentation quality only. It cannot prove physical
wiring.
