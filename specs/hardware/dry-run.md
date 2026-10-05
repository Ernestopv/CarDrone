# Hardware Dry-Run Mode

## Purpose

Define the `HARDWARE_MODE=dry-run` runtime that exercises the Raspberry Pi
hardware command path while preventing physical GPIO/PWM output from being
applied.

Dry-run is a safety mode, not a second command implementation:

```text
semantic command
    ↓
application/controller flow
    ↓
hardware provider and mappings
    ↓
dry-run output boundary
    ├── log intended operation
    └── suppress physical write
```

The mode must make intended operations observable and must never claim that a
motor moved or that hardware confirmed a command.

## Dependencies

- `specs/architecture/runtime-deployment.md` — D3/D5, runtime mode semantics,
  strict configuration, and verification levels.
- `specs/hardware/hardware-abstraction.md` — `IDroneHardware` and software-level
  `Applied` semantics.
- `specs/hardware/raspberry-hardware-provider.md` — current composition-root
  selection and Raspberry provider boundary.
- `specs/hardware/gpio.md` — `IGpioController`, configuration binding, and the
  Infrastructure-only GPIO seam.
- `specs/hardware/failsafe.md` — timeout, STOP, recovery, and fail-closed
  behavior that dry-run must preserve.
- `specs/docker/raspberry-hardware-access.md` — runtime/device boundary; dry-run
  must not broaden device or privilege exposure.
- Task 28 motor wiring and Task 30 PWM specifications — future operation
  mappings and PWM values that dry-run must log without inventing.
- Existing `DroneRuntimeSelection`, `IDroneController`, API wire contract, and
  backend test conventions.

## Scope

- Accept `HARDWARE_MODE=dry-run` at the composition root instead of rejecting
  it as reserved.
- Select the same hardware-backed command flow used by `real` mode.
- Suppress physical GPIO/PWM writes at the lowest output boundary before they
  can energize hardware.
- Log structured intended GPIO/PWM operations, including that they were
  suppressed and not physically applied.
- Preserve command ordering, validation, timeout, STOP, failure, and recovery
  behavior from the normal hardware path and Task 26.
- Keep dry-run configuration-driven and independent of frontend behavior.
- Provide mock/fake tests on a development machine and, when available, a
  dry-run test on the Raspberry Pi runtime.
- Keep the HTTP endpoint set and JSON contract unchanged.

## Out of Scope

- Implementing or changing GPIO line semantics, L298N wiring, motor direction,
  PWM frequency, duty-cycle limits, or electrical safety values.
- Claiming that a dry-run operation physically changed a GPIO/PWM output,
  energized a motor, or confirmed movement.
- Creating a separate controller, separate semantic command flow, or remote Pi
  service.
- Implementing fail-safe policy; Task 26 owns the policy. Dry-run must obey it.
- Implementing camera/uStreamer behavior, frontend changes, authentication, or
  new API endpoints.
- Using `privileged: true`, broad device exposure, or a fake device mapping as a
  shortcut to make dry-run work.
- Silently falling back to `mock` when dry-run configuration or prerequisites
  are invalid.

## Architecture

```text
HARDWARE_MODE
       ├── mock    → existing MockDroneController
       ├── dry-run → HardwareDroneController
       │               → Raspberry hardware/provider path
       │               → dry-run GPIO/PWM output boundary
       │                    → structured log, no physical write
       └── real    → HardwareDroneController
                       → Raspberry hardware/provider path
                       → verified GPIO/PWM output
```

The suppression boundary must be below semantic command mapping and above the
physical write. This allows future Tasks 28–30 to calculate intended GPIO/PWM
operations through the same path while the dry-run sink prevents output.

If a platform API opens a resource before writing, the implementation must
document whether opening is required for dry-run and prove that no output can
be applied. It must not rely on an undocumented library side effect for safety.

Mode selection remains at the composition root. Application and Domain code do
not branch on dry-run. The API continues to see the existing controller and
wire shapes only.

## Domain Model

No new Domain types are required.

The Infrastructure dry-run operation record must distinguish intent from
application. It should contain, at minimum:

- operation category: GPIO or PWM;
- configured identifier/reference, without adding semantic motor meaning;
- requested value/parameters as supplied by the hardware path;
- timestamp or sequence number sufficient to verify ordering;
- `dryRun=true`;
- `physicallyApplied=false`;
- suppression reason.

The record/log must not use `confirmed`, `hardware acknowledged`, or equivalent
language for a suppressed operation.

`HardwareAvailability.Available` in dry-run means that the dry-run software path
is initialized and able to record/suppress operations. It does not mean that a
physical device is available or that a motor can move. The existing wire has no
dedicated dry-run field; clients must not interpret the existing status or
software-level `Applied` result as physical confirmation.

## Behavior

### Configuration and selection

- Unset `HARDWARE_MODE` remains `mock`.
- `HARDWARE_MODE=dry-run` is parsed case-insensitively and with the same
  trimming/strictness rules as the other modes.
- Invalid values fail startup with an explicit accepted-values message.
- Dry-run must pass the supported Raspberry runtime preflight required by the
  provider path. The default supported target is `linux/arm64`; any Pi-like
  exception requires an explicit, separately documented policy.
- Invalid or missing dry-run configuration fails clearly; it never becomes
  `mock` or `real`.
- The same source and Compose entry point are used; only configuration selects
  the mode.

### Operation suppression

- Every intended GPIO/PWM operation reaches the dry-run output boundary.
- The boundary records the operation before suppressing it.
- No physical write, duty-cycle application, motor energization, or equivalent
  output is performed.
- Operation ordering and failure behavior match the real path as far as the
  output boundary permits.
- A failure to record an intended operation is a dry-run failure; it must not
  fall through to a physical write.
- An unknown output operation is rejected and logged as suppressed/error; it
  must not be executed physically.

### Commands and speed

- Semantic commands, validation, connection rules, and speed range validation
  use the same path as real mode.
- `STOP` is passed through the same fail-safe path and is logged as intended but
  suppressed. Dry-run does not prove that physical STOP works.
- The controller may return the existing software-level result needed to keep
  the flow testable, but the result must remain distinguishable in logs and
  implementation from real hardware confirmation.
- Command timeout, hardware-like failure, cancellation, and recovery follow
  Task 26. Dry-run must not bypass safety transitions because writes are
  suppressed.

### Logs and observability

- Logs are structured and include mode, operation category, operation sequence,
  configured identifier, requested parameters, and suppression status.
- Logs must explicitly state that the operation was intended and suppressed.
- Logs must not imply physical movement or hardware acknowledgement.
- Logging must not expose secrets or unrelated host information.
- Log failure is fail-closed: the operation is not forwarded to the real sink.

### Recovery and shutdown

- Startup, recovery, timeout, fault, disconnect, and graceful shutdown execute
  the same dry-run safety flow as real mode.
- A dry-run STOP is recorded/suppressed, not physically tested.
- Restart begins in the conservative state defined by Task 26; dry-run does not
  inherit a prior command as active.

## Interfaces

Existing public contracts remain unchanged:

```text
IDroneController
IDroneHardware
HTTP API and frontend wire contract
```

The implementation may add Infrastructure-only abstractions equivalent to:

```text
IHardwareOutputSink
  RealHardwareOutputSink
  DryRunHardwareOutputSink
```

or decorators over the GPIO/PWM interfaces defined by Tasks 25 and 30. The
required properties are:

- real and dry-run use the same upstream operation flow;
- only the final output sink differs;
- dry-run has no path to physical write;
- operation records/logs are emitted before suppression;
- cancellation and failures propagate consistently;
- no Domain/Application dependency on the sink or mode exists.

No new HTTP endpoint or response field is required.

## Validation

- Mode parsing accepts only `mock`, `dry-run`, and `real`.
- Dry-run platform and configuration preflight is explicit and fail-fast.
- Every GPIO/PWM operation in dry-run produces an observable suppression record.
- Every suppression record has `physicallyApplied=false` and `dryRun=true`.
- Tests prove the dry-run sink does not invoke the real output sink.
- Tests prove a logging failure cannot fall through to real output.
- Tests prove invalid commands, speeds, identifiers, and output parameters are
  rejected consistently with real mode.
- Tests prove STOP, timeout, cancellation, and recovery use the same path.
- No dry-run result is mapped to a hardware-confirmed acknowledgement.

## Error Cases

- Invalid `HARDWARE_MODE` → startup failure listing valid modes.
- Dry-run selected on an unsupported runtime → startup failure; no mock
  fallback.
- Missing/invalid configuration → startup failure before command acceptance.
- Unknown GPIO/PWM operation → rejected and suppressed; never forwarded.
- Dry-run log/record failure → operation fails closed; no physical fallback.
- Underlying path validation or provider failure → existing unavailable/error
  behavior and Task 26 safety transition.
- Timeout/cancellation during a dry-run operation → recorded as a suppressed
  or failed dry-run event according to the observed stage; no physical write.
- Attempt to use a real output sink while dry-run is selected → startup/test
  failure indicating an unsafe composition.
- Attempt to report physical confirmation → rejected by implementation/tests;
  dry-run remains software-only.

## Platform Requirements

- Development PC: mock/fake dry-run tests must not require Raspberry devices or
  physically accessible GPIO/PWM interfaces.
- Raspberry runtime: dry-run should run on the verified `linux/arm64` software
  stack when the target Pi and Task 24/25 prerequisites are available.
- The backend remains non-root and must not require broader device permissions
  than the real path merely to suppress output.
- Docker/Compose configuration remains configuration-driven; no source edits or
  alternate service are required to switch modes.
- The actual Pi runtime, device paths, permissions, and API support remain
  Task 24 prerequisites and must not be guessed here.

## Security / Safety

- Dry-run is fail-closed: uncertainty results in suppression or failure, never
  real output.
- The real output sink must be structurally unreachable in a dry-run graph, not
  merely disabled by a mutable boolean at call time.
- Do not use a runtime flag that can silently change from dry-run to real while
  the process is running.
- Do not claim that `Applied`, `Connected`, or a successful log entry means
  physical actuation or confirmation.
- Preserve Task 26 timeout and STOP behavior even though output is suppressed.
- Do not treat dry-run testing as real-hardware testing.

## Testing Scenarios

### Development-PC tests (`mock-tested`)

1. Mode parser accepts `dry-run`, rejects unknown/empty values, and preserves
   `mock` as the default.
2. Composition tests resolve the hardware-backed flow plus dry-run output sink
   and cannot resolve a real output sink for the same graph.
3. A semantic command produces the same intended operation sequence as the
   real-path fake, while every output is recorded as suppressed.
4. GPIO and PWM fake operations are logged with `dryRun=true` and
   `physicallyApplied=false`.
5. The real output fake records zero writes in dry-run mode.
6. Log failure, invalid operation, cancellation, timeout, exception, and
   recovery remain fail-closed.
7. STOP is sent through the same safety path and is suppressed, not omitted.
8. Existing API/frontend tests remain unchanged; no new wire field is needed.

These tests prove composition and software suppression only. They do not prove
that a real Pi cannot energize a motor or that the physical STOP state works.

### Raspberry Pi dry-run tests (`dry-run-tested`)

When the target device is available:

1. Run the same ARM64 image and configuration intended for the hardware path.
2. Confirm the provider, GPIO/PWM configuration, permissions, and dry-run sink
   initialize under the actual non-root container user.
3. Exercise connect, commands, speed, STOP, timeout, failure, recovery, and
   shutdown.
4. Confirm logs contain intended operations and explicit suppression markers.
5. Confirm the real output sink reports zero physical writes according to the
   verified adapter/runtime evidence.

This proves dry-run suppression at the runtime level, not motor direction,
electrical safety, or real actuation.

### Not proven by Task 27

- L298N wiring or motor-direction correctness.
- PWM frequency/duty safety or speed response.
- Physical STOP behavior.
- Protection against power loss or abrupt process failure unless separately
  verified by Task 26's external mechanism.

## Acceptance Criteria

- [x] `HARDWARE_MODE=dry-run` is accepted by strict composition-root parsing;
  invalid values still fail and unset mode still defaults to `mock`.
- [x] Dry-run uses the same semantic command, validation, controller, provider,
  GPIO/PWM mapping, timeout, STOP, and recovery flow as real mode.
- [x] Physical output is suppressed at the lowest GPIO/PWM output boundary;
  the real output sink is structurally unreachable in the dry-run graph.
- [x] Every intended GPIO/PWM operation produces a structured record identifying
  dry-run mode and `physicallyApplied=false`.
- [x] Logging or suppression failure cannot fall through to physical output and
  leaves the safety path faulted according to Task 26.
- [x] Mock/fake tests prove zero physical writes, correct operation ordering,
  STOP handling, timeout/cancellation behavior, and recovery behavior.
- [x] Dry-run does not require frontend changes, new API endpoints, new wire
  fields, or a separate controller/service.
- [x] Dry-run does not silently fall back to `mock` or `real` on invalid
  configuration or unsupported runtime.
- [x] If tested on Raspberry Pi, results are labeled `dry-run-tested`; no claim
  of `real-hardware-verified` is made from dry-run alone.
- [x] GPIO levels, L298N mappings, motor directions, PWM values, electrical
  limits, and physical STOP behavior are not invented by this task.
