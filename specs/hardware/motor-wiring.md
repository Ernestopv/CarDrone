# L298N Motor Wiring Documentation

## Purpose

Define the content, evidence rules, and status vocabulary of the single source
of truth for the physical wiring between the Raspberry Pi, the L298N motor
driver, and the motors: `docs/hardware/WIRING.md`.

Task 28 is a documentation task. It records what is physically connected when
that is known, and marks everything else explicitly `NOT VERIFIED`. It derives
nothing from pin numbers, ordering, or examples. The document exists so that
later tasks (Task 29 direction mapping, Task 30 PWM, future actuation) consume
confirmed wiring instead of guessing.

Current evidence state at planning time (operator decision): only the GPIO
identifier configuration is known. The Pi-to-L298N mapping, motor terminals,
power, common ground, and the STOP electrical state are NOT yet established,
so they are documented as unknowns — not filled in by inference.

## Dependencies

- `specs/architecture/runtime-deployment.md` — prerequisites ledger
  (pin↔L298N semantics owned by Task 28) and the verification ladder.
- `specs/hardware/hardware-abstraction.md` — `IDroneHardware` is the seam
  below which wiring matters; wiring knowledge must never appear above it.
- `specs/hardware/gpio.md` — configured identifiers (`Pin1..Pin4`, `PWM1`,
  `PWM2`) carry no motor or electrical meaning.
- `specs/hardware/raspberry-hardware-provider.md` — the provider stays inert
  while wiring is unverified; this document is the evidence that unblocks
  later actuation tasks.
- `specs/hardware/dry-run.md` — dry-run suppression exists precisely because
  wiring is unconfirmed; this task does not change that behavior.
- `specs/docker/raspberry-hardware-access.md` and
  `docs/hardware/raspberry-pi-inventory.md` — device/permission boundary.
  The inventory explicitly does NOT verify motor wiring; the two documents
  must not duplicate or contradict each other.

## Scope

- Create `docs/hardware/WIRING.md` containing every required field, each with
  an explicit status:

  - Raspberry GPIO → L298N input mapping: `IN1`, `IN2`, `IN3`, `IN4`;
  - `ENA` control as physically present (jumper, external drive, or driven by
    `PWM1` — record what exists, do not presume);
  - `ENB` control as physically present (jumper, external drive, or driven by
    `PWM2`);
  - Motor A terminals;
  - Motor B terminals;
  - motor terminal orientation, recorded as observed physical positions or
    labels (never as forward/backward);
  - power wiring assumptions (what supply connects where, as observed);
  - ground / common-ground requirements between Pi, L298N, and supply, as
    observed;
  - chosen STOP electrical state (the output condition intended to stop the
    motors), recorded only if actually chosen or observed.

- Record the known GPIO configuration as given configuration data.
- Attach a per-field status using the vocabulary defined below.
- Include a field-by-field confirmation checklist so the operator can
  complete the document later on the physical hardware.
- Include a "safety-critical unknowns" section near the top of the document.
- Update durable status documentation for the task result (e.g. `MEMORY.md`,
  `docs/ARCHITECTURE.md` status lines) as part of the implementation, without
  copying the wiring content into those files.

## Out of Scope

- Defining `forward`/`backward`/`left`/`right` motor meaning — Task 29
  observes physical direction first.
- PWM frequency, duty cycles, or safe speed envelopes — Task 30.
- Electrical limits (voltages, currents) unless read from physical device
  markings or operator-supplied references; never guessed.
- Any application code, configuration value, Compose, or frontend change.
- Inferring a mapping from GPIO numbers, sequence (`Pin1` first ⇒ `IN1`
  first), or internet/example wiring.
- Changing the GPIO configuration values themselves.
- Verifying wiring in software: mock/dry-run tests cannot verify physical
  wiring and must not be presented as such.
- Camera, uStreamer, or device/permission documentation — owned by the
  inventory and later camera tasks.

## Architecture

```text
docs/hardware/WIRING.md          ← Task 28 deliverable (documentation only)
        │
        │ read by humans / referenced by specs — never by code
        ↓
Task 29 (direction mapping) / Task 30 (PWM) / future actuation specs
        ↓
IDroneHardware implementations (Infrastructure, below the seam)
        ↓
GPIO configuration (identifiers only: Pin1..Pin4, PWM1, PWM2)
```

Rules:

- `WIRING.md` is documentation. No code path reads or parses it; it informs
  specifications, not runtime behavior.
- Nothing above Infrastructure ever learns pin mappings; the frontend never
  sees GPIO details (existing hard rule, unchanged).
- Wiring values are NOT copied into `appsettings.json` by this task; the
  `GPIO` section stays identifier-only. If a future task decides that a
  mapping must become configuration, that is a separate specification.
- The document complements `docs/hardware/raspberry-pi-inventory.md`
  (devices/permissions) instead of duplicating it.

## Domain Model

### Wiring record

Every required field in `WIRING.md` is a record of:

```text
field name
value (or explicit TBD marker)
status: CONFIRMED | NOT VERIFIED
evidence note + date — required only when CONFIRMED
```

Status vocabulary:

- `CONFIRMED` — the operator physically established this connection
  (inspection, photo reference, or measurement actually performed). The
  evidence note and date must be part of the record; a status without
  evidence does not count as CONFIRMED.
- `NOT VERIFIED` — not yet established from physical evidence. The field is
  still present in the document with a TBD marker so the document cannot be
  mistaken for complete.

There is no third status. "Probably", "standard wiring", or "assumed" are
not statuses.

### Given configuration (recorded, not derived)

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

This block is configuration: it names which pins the software may use. It
assigns no motor meaning, and it does not state which pin reaches which
L298N terminal. Whether `PWM1`/`PWM2` connect to `ENA`/`ENB` at all is
itself a wiring fact to be recorded, not an assumption.

## Behavior

### Documentation rules

1. Every required field appears in `WIRING.md` regardless of how much is
   known. Nothing is omitted to make the document look finished.
2. A field becomes `CONFIRMED` only through operator-provided physical
   evidence, recorded with source and date in the document.
3. Unknown fields are marked `NOT VERIFIED` with a TBD marker; they are never
   silently dropped or backfilled.
4. Pattern inference is forbidden: deriving `Pin1 → IN1`, `PWM1 → ENA`, or
   any similar ordering-based mapping is rejected unless the operator
   confirms it as the observed physical connection. The confirmation is the
   evidence — the pattern never is.
5. Orientation is direction-neutral: terminals are recorded by their physical
   position/label as observed, never as front/back/left/right or
   forward/backward.
6. The STOP electrical state is documented only if actually chosen or
   observed. While undecided, it is a `NOT VERIFIED` safety-critical unknown
   and the document must state that hardware STOP semantics are undefined;
   downstream tasks may not assume a STOP level.
7. Conflicts between the documented wiring and the GPIO configuration are
   reported as an open issue in the document. Neither side is edited
   silently to make them agree.
8. Later updates follow the same rules: a status changes only when new
   evidence arrives, and a disproven `CONFIRMED` field is downgraded to
   `NOT VERIFIED` with a note.

### Software behavior

None. No runtime path, runtime mode (`mock`/`dry-run`/`real`), configuration
binding, or test behavior changes as a result of this task.

## Interfaces

- Documentation deliverable only: `docs/hardware/WIRING.md`.
- The document references the GPIO configuration and the prerequisites
  ledger; Task 29 and Task 30 specifications must list `WIRING.md` as a
  dependency when they are planned.
- No code interfaces, no configuration contract change, no HTTP endpoint or
  wire change.

## Validation

Performed at implementation time:

- Completeness: every required field listed under Scope is present.
- Status discipline: every field carries `CONFIRMED` (with evidence + date)
  or `NOT VERIFIED`.
- Consistency: the GPIO block recorded in `WIRING.md` matches
  `backend/src/DroneControl.Api/appsettings.json` exactly, and matches the
  configuration documented in `AGENTS.md`/`docs/PRD.md`.
- No-inference review: no field is derived from pin numbers or ordering;
  no direction or motor-action semantics exist anywhere in the document.
- Forbidden-content review: no PWM frequency/duty values, no electrical
  limits, no physical STOP behavior claims, no unqualified "verified".
- Regression: existing validation still passes — `dotnet build`,
  `dotnet test`, `npm test` — because no code changed.
- Cross-references: every path mentioned in the document exists.

## Error Cases

- Missing physical information → the field is recorded `NOT VERIFIED`.
  This does not block the documentation task; physical confirmation is
  deferred and reported at the stated verification level.
- Documented wiring conflicts with the GPIO configuration → record an open
  issue in `WIRING.md` and report it; do not auto-correct either source.
- A request to "fill in a plausible value" → rejected by these rules; the
  field stays `NOT VERIFIED`.
- Evidence later disproves a `CONFIRMED` field → downgrade the status with a
  note (rule 8 above).
- Treating this document as authorization to energize hardware → rejected:
  the Task 26/27 gates (including the D7 real-mode gate) are unaffected by
  documentation existing.

## Platform Requirements

- Creating `WIRING.md` requires no Raspberry Pi, devices, or containers — it
  is plain documentation on any development machine.
- Confirming `NOT VERIFIED` fields requires physical access to the wired
  hardware; unless the operator supplies that evidence during
  implementation, those fields remain `NOT VERIFIED` at task completion and
  are reported as such.
- No Docker/Compose, platform, or permission changes.

## Security / Safety

- The document never claims physical verification it does not have; every
  claim carries evidence or an explicit `NOT VERIFIED`.
- The "safety-critical unknowns" section (STOP state, unconfirmed mapping)
  must be visible near the top of `WIRING.md` so a reader cannot miss what
  is unknown before touching hardware.
- No field may imply that a motor can move or that a STOP condition is
  guaranteed; software-level results remain distinguishable from physical
  behavior (unchanged honesty rules).
- Dry-run/real gating, fail-safe behavior, and the D7 gate are untouched by
  this task.

## Testing Scenarios

### Document review (development PC — no hardware required)

1. Completeness checklist: every Scope field exists in `WIRING.md`.
2. Status checklist: every field has one of the two allowed statuses;
   `CONFIRMED` entries carry evidence and a date.
3. Consistency check: GPIO block in `WIRING.md` equals the backend
   configuration values (23, 24, 21, 20, 12, 13).
4. Negative review: the document contains no direction semantics, no PWM
   values, no electrical limits, no inference-based mapping presented as
   fact, and no unqualified verification claims.
5. Regression: `dotnet build`, `dotnet test`, `npm test` still pass,
   confirming the task changed no code.
6. Cross-reference check: linked documents exist.

These reviews prove documentation quality only. They cannot prove physical
wiring.

### Physical confirmation (when the hardware is available — deferred)

The operator completes the confirmation checklist on the actual bench and
updates each field with evidence, moving it to `CONFIRMED`. Only then may the
affected wiring facts be reported at the `real-hardware-verified` level.
This scenario is intentionally NOT a mandatory acceptance criterion for
Task 28: at planning time only the GPIO identifiers are known.

### Not proven by Task 28

- Which GPIO actually reaches `IN1..IN4`/`ENA`/`ENB`.
- Motor terminal orientation or resulting direction.
- Power/ground correctness on the physical build.
- STOP behavior, PWM behavior, or any electrical safety property.

## Acceptance Criteria

- [x] `docs/hardware/WIRING.md` exists.
- [x] It contains every required field (IN1, IN2, IN3, IN4, ENA, ENB,
      Motor A, Motor B, terminal orientation, power wiring, common ground,
      STOP electrical state), each with an explicit status.
- [x] The given GPIO configuration is recorded in the document and matches
      `appsettings.json` exactly (23, 24, 21, 20, 12, 13).
- [x] Every field is either `CONFIRMED` with evidence and date or
      `NOT VERIFIED`; with the currently available data, all Pi-to-L298N
      mapping fields are `NOT VERIFIED`.
- [x] No mapping is derived from pin numbers or ordering; any asserted
      connection is operator-confirmed evidence.
- [x] The document defines no motor direction semantics, no PWM
      frequency/duty values, no electrical limits, and no physical STOP
      behavior claim.
- [x] A safety-critical unknowns section is present and lists the
      unconfirmed mapping and STOP-state unknowns.
- [x] A confirmation checklist exists so each `NOT VERIFIED` field can be
      completed later on the physical hardware.
- [x] The document cross-references existing paths only (inventory doc,
      runtime prerequisites ledger).
- [x] No application source code, configuration values, Compose files, or
      frontend files were modified; `dotnet build`, `dotnet test`, and
      `npm test` results are unchanged from the pre-task baseline.
