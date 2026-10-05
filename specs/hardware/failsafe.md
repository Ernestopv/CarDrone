# Hardware Fail-Safe

## Purpose

Define the software and runtime safety policy that prevents a previously
requested movement from remaining active indefinitely when the control path
fails.

The normative safe command is the semantic `DroneCommand.Stop`. This task
defines when the control path must request that command, how failures and
timeouts transition the software to a safe state, and what must be verified
before claiming a physical stop.

This specification does not invent the GPIO levels, L298N wiring, PWM behavior,
or electrical stop state needed to make `STOP` physically safe.

## Dependencies

- `specs/architecture/runtime-deployment.md` — D3/D5, runtime modes, safety
  policy, acknowledgement honesty, and verification levels.
- `specs/hardware/hardware-abstraction.md` — `IDroneHardware`, semantic
  `DroneCommand.Stop`, cancellation, and software-level `Applied` semantics.
- `specs/hardware/raspberry-hardware-provider.md` — provider boundary and
  existing unavailability/error contracts.
- `specs/docker/raspberry-hardware-access.md` — container device and permission
  boundary.
- `specs/hardware/gpio.md` — low-level GPIO seam; no motor meanings are defined
  there.
- `tasks/BACKLOG.md` Tasks 25–30 — GPIO, fail-safe, dry-run, wiring, direction,
  and PWM ownership boundaries.
- Existing `IDroneController` and API error contracts — the HTTP surface must
  remain unchanged unless a later approved contract specification says
  otherwise.

## Scope

- Define the safety state machine for startup, normal command execution,
  command timeout, hardware failure, invalid input, connection loss, graceful
  shutdown, restart, and recovery.
- Ensure movement commands are supervised by a bounded command/liveness
  timeout so an old movement cannot remain active indefinitely in the software
  control path.
- Ensure a safety transition cancels or supersedes in-flight movement work and
  requests semantic `STOP` through the existing hardware abstraction.
- Define bounded stop-attempt behavior, logging, failure reporting, and the
  conditions under which recovery may be allowed.
- Keep safety logic below the application/API contract and independent of
  frontend behavior.
- Define mock and dry-run test expectations without treating them as physical
  verification.
- Identify the external mechanisms required for process crash, power loss,
  kernel failure, and abrupt container termination.

## Out of Scope

- Defining GPIO `High`/`Low` combinations or the physical L298N STOP state.
- Defining which GPIO controls any L298N input, enable line, or motor.
- Defining motor direction, PWM frequency, duty-cycle limits, or electrical
  limits; those belong to Tasks 28–30.
- Implementing dry-run logging/suppression; Task 27 owns that implementation.
- Camera/uStreamer failure handling, frontend safety UI, authentication, or a
  new HTTP endpoint.
- Claiming that a semantic STOP request physically stopped a motor.
- Claiming protection against abrupt power loss, kernel failure, or process
  crash without a separately verified external hardware/runtime mechanism.
- Choosing a watchdog device, GPIO pull state, systemd policy, Docker runtime
  option, or Raspberry Pi-specific device path without verified prerequisites.

## Architecture

```text
React / API
     ↓ existing semantic command contract
IDroneController / application flow
     ↓
Safety coordinator or decorator                 (Task 26)
     ├── command serialization and timeout
     ├── fault transition and cancellation
     ├── semantic DroneCommand.Stop request
     └── bounded recovery policy
     ↓
IDroneHardware
     ↓
GPIO abstraction / provider / runtime boundary
```

The implementation may use a controller decorator, hardware decorator, or a
dedicated Infrastructure safety coordinator, but it must preserve these
boundaries:

- Domain and frontend remain unaware of GPIO and safety implementation details.
- API controllers do not manipulate GPIO or own safety state.
- All movement and STOP operations pass through the same hardware abstraction
  path; safety must not create a parallel command path.
- The composition root selects one complete graph for `mock`, `dry-run`, or
  `real`; safety behavior must not be scattered as mode checks through business
  logic.

Graceful host shutdown may use the .NET hosted-lifecycle boundary. Ungraceful
process termination and power loss require an external or hardware-level
mechanism and remain an explicit prerequisite until verified.

## Domain Model

The safety state is an Infrastructure/Application-support concept and must not
expose hardware details:

- `Safe` — no movement is considered active by the supervised software path.
- `CommandActive` — a movement command was accepted and remains within its
  liveness window.
- `StopPending` — a safety transition has requested STOP and is awaiting the
  bounded stop result.
- `Faulted` — the safety transition or hardware availability is not proven;
  movement commands are blocked until the configured recovery rule succeeds.
- `Recovering` — an explicit recovery operation is re-establishing the safe
  baseline before commands may be accepted again.

The exact public representation is not an HTTP contract. Existing
`DroneStatus` fields remain unchanged unless a later specification explicitly
extends them.

Safety events should carry structured reason categories such as:

- startup;
- command timeout;
- hardware exception/unavailable;
- connection loss;
- graceful shutdown;
- container/process restart;
- invalid or rejected operation.

They must not claim a physical cause that has not been observed.

## Behavior

### Safe default

- Startup begins in a conservative state: physical safety is **unverified**.
- The implementation must establish the software safe baseline before
  accepting movement commands. Where the hardware path is available, this
  includes requesting semantic `STOP` through `IDroneHardware`.
- If the baseline cannot be established, the system remains unavailable or
  faulted and does not accept movement commands.
- A successful software-level STOP result is not physical stop confirmation.

### Command supervision

- At most one movement operation may be active for a hardware session unless
  the selected provider explicitly supports safe serialization.
- Every accepted movement command receives a bounded liveness deadline from
  configuration.
- A new command supersedes the previous command only through the serialized
  safety path; stale completions must not restore an older movement state.
- When the deadline expires, the coordinator cancels the movement operation,
  enters `StopPending`, and requests `DroneCommand.Stop`.
- A command that fails or is rejected cannot update confirmed state as though it
  succeeded.

### Hardware exception or unavailability

- A hardware exception, unavailable result, lost provider availability, or
  failed write transitions the supervised path to `StopPending`.
- The coordinator makes a bounded best-effort STOP request.
- If STOP cannot be sent or does not complete within the stop deadline, the
  state becomes `Faulted`; movement commands remain blocked.
- The existing `DroneUnavailableException`/503 channel remains the application
  error boundary. No silent fallback to mock is permitted.

### Invalid command or input

- Invalid commands are rejected before reaching GPIO or the hardware adapter.
- The existing API validation behavior remains authoritative and is not
  bypassed by the safety layer.
- An invalid request must not extend an active movement deadline or overwrite a
  valid safety state. The independent liveness timeout remains in force.

### Connection loss

- Loss of the controller session, provider availability, or command liveness
  enters the same STOP/fault path as a hardware failure.
- Recovery must not require frontend participation.
- Reconnection does not immediately authorize movement. The safe baseline must
  be re-established first.

### Graceful shutdown

- Application shutdown requests STOP through the same semantic hardware path.
- Shutdown uses a bounded stop deadline and logs whether the software-level
  STOP request completed, failed, or was unavailable.
- Resources are then disposed/released according to the GPIO/provider
  contract.
- Shutdown success must not be described as physical stop confirmation.

### Crash, power loss, restart, and Docker restart

- A normal host/container shutdown follows the graceful shutdown behavior.
- A process crash, forced kill, kernel failure, Raspberry Pi reboot, or power
  loss may prevent .NET code from sending STOP. The implementation must not
  claim that an in-process handler protects these cases.
- Before real hardware is enabled, the project must identify and verify the
  external mechanism that makes outputs safe for these abrupt failures, or
  explicitly block real operation until such a mechanism exists.
- The software gate is `Safety:ExternalAbruptFailureProtectionVerified`,
  default `false` and passed as `SAFETY__EXTERNALABRUPTFAILUREPROTECTIONVERIFIED`.
  When `HARDWARE_MODE=real` and this flag is false, the safety coordinator stays
  `Faulted` and performs no controller/hardware operation. Set it true only
  after the external mechanism has its own verified evidence; setting the flag
  itself is not that evidence.
- After restart, the system starts unavailable/unverified, re-establishes the
  safe baseline, and only then permits movement.

### Recovery

- Recovery is explicit and fail-closed.
- The provider must be available, the safe baseline must complete within its
  deadline, and no stale command may remain in flight before leaving
  `Recovering`.
- Automatic retry count and delays must be bounded and configuration-driven;
  unbounded retries are forbidden.
- A failed recovery remains `Faulted` and does not fall back to mock.

### Runtime modes

- `mock`: executes the same safety state transitions against mock hardware and
  records semantic STOP requests; no physical claim is made.
- `dry-run`: must preserve the same safety transitions and timing policy, while
  Task 27 owns suppression/logging of physical operations. Task 26 does not
  implement dry-run here.
- `real`: uses the verified provider/GPIO path. Physical STOP claims remain
  blocked until wiring and hardware verification are complete.

## Interfaces

The implementation must preserve `IDroneHardware` and `IDroneController` public
contracts. It may introduce an internal safety abstraction with equivalent
responsibilities:

```text
StartSafetyAsync
ExecuteSupervisedCommandAsync
HandleFailureAsync
RequestStopAsync
RecoverAsync
StopSafetyAsync
```

Required properties:

- cancellation tokens are propagated;
- command and STOP operations are serialized;
- timeout and stop deadlines are explicit and configurable;
- stale asynchronous completions cannot change the current safety state;
- safety events are structured and logged without exposing secrets or claiming
  physical actuation.

Configuration contract:

- `Safety:CommandTimeoutMilliseconds` — required positive bounded movement and
  operation deadline.
- `Safety:StopTimeoutMilliseconds` — required positive bounded deadline for
  each semantic STOP attempt.
- `Safety:StopRetryCount` and `Safety:StopRetryDelayMilliseconds` — required,
  bounded stop retry policy.
- `Safety:RecoveryRetryCount` and `Safety:RecoveryRetryDelayMilliseconds` —
  required, bounded baseline recovery policy.
- `Safety:ExternalAbruptFailureProtectionVerified` — defaults false; real mode
  remains faulted until an external protection mechanism is separately
  verified. Mock mode does not require this flag to be true.

No new frontend/API endpoint is required for this task.

## Validation

- Timeout values must be present, parseable, positive, and bounded by an
  implementation-defined safety maximum; invalid configuration fails fast.
- Stop timeout and retry limits must be independently validated.
- Movement commands must not be accepted while `StopPending`, `Faulted`, or
  `Recovering`.
- STOP must be attempted through the same `IDroneHardware` abstraction and
  cannot be replaced with a direct GPIO shortcut.
- A stale command completion after timeout, disconnect, or STOP must not alter
  the active safety state.
- Recovery must verify availability and the safe baseline before reopening
  command acceptance.
- No real-mode success is accepted if GPIO wiring, STOP electrical semantics,
  or the external abrupt-failure mechanism is unknown.
- Configuration and state transitions must be deterministic in mock tests.

## Error Cases

- Missing/invalid timeout or retry configuration → startup/configuration error.
- Command exceeds its deadline → cancellation, STOP attempt, then `Safe` or
  `Faulted` depending on the STOP result.
- Movement operation throws → STOP attempt; failure leaves the system faulted.
- STOP operation throws, times out, or hardware is unavailable → `Faulted`,
  movement blocked, explicit log/error boundary.
- New movement while faulted or recovering → rejected without hardware access.
- Recovery timeout/failure → remains `Faulted`; no mock fallback.
- Real mode without the external-protection verification flag → remains
  `Faulted` before connecting or issuing any controller operation.
- Graceful shutdown STOP failure → shutdown continues after the bounded attempt,
  but the result is logged as unverified/failed.
- Abrupt process/power failure → software fail-safe status is unknown unless an
  external mechanism has been verified; never report success by assumption.

## Platform Requirements

- Must build and test with the existing .NET 10 solution on the development PC.
- Mock tests must not require Raspberry Pi, Docker devices, GPIO libraries, or
  physical motors.
- Real-mode execution depends on the Task 24 device/permission inventory and
  Task 25 GPIO adapter.
- Physical STOP semantics depend on Task 28 wiring and subsequent direction/
  PWM work; this task must not invent those values.
- Docker restart and graceful signal behavior must be tested separately from
  an ungraceful kill or power-loss scenario.

## Security / Safety

- Fail closed: uncertainty or failure blocks further movement.
- Safety must not depend on React, browser focus, network timing, or a user
  pressing STOP.
- Do not claim physical STOP from a returned `Applied` result; that result is
  software-level under the Task 22 contract.
- Avoid unbounded retries and background work that can outlive the safety
  coordinator without a cancellation/ownership policy.
- Do not use privileged containers, invented GPIO paths, or unverified
  watchdog/device mappings as a safety mechanism.
- Any external watchdog or electrical default must be documented and tested at
  its own verification level before real-hardware claims.

## Testing Scenarios

### Mock-testable without a Raspberry Pi

1. Startup begins unavailable/unverified and accepts movement only after the
   mock safe baseline succeeds.
2. A movement command enters `CommandActive`; expiry cancels it and records a
   semantic STOP request.
3. A command exception, unavailable hardware result, or rejected operation
   enters the STOP/fault path.
4. STOP success returns to `Safe`; STOP timeout/exception enters `Faulted`.
5. Commands are blocked while `StopPending`, `Faulted`, and `Recovering`.
6. Disconnect, cancellation, and stale in-flight completion cannot restore an
   older movement state.
7. Recovery requires availability plus a successful safe baseline and uses
   bounded retries.
8. Graceful shutdown requests STOP within its deadline.
9. Mock and fake hardware tests verify serialization, timeout values, logging
   reason categories, and no duplicate stale completion effects.
10. Existing API wire behavior and frontend tests remain unchanged.

These prove software policy only. They do not prove electrical STOP behavior,
GPIO access, motor response, Docker device permissions, or crash/power-loss
protection.

### Requires Pi/runtime or external-mechanism verification

1. The real provider can execute semantic STOP using the Task 24/25 runtime
   boundary.
2. The configured non-root container can access the required GPIO/PWM resources.
3. The actual wiring produces the documented safe physical state.
4. Graceful container shutdown leaves the real hardware in the verified safe
   state.
5. A verified watchdog, GPIO default, driver behavior, or equivalent external
   mechanism protects abrupt process/container failure, Pi restart, or power
   loss.

### Verification labels

- Software state-machine tests: `mock-tested`.
- ARM64 build: `built` only.
- Real provider running on the target Pi without physical motor claims:
  `Pi-runtime-verified`.
- Observed safe physical STOP under each required failure scenario:
  `real-hardware-verified`.

## Acceptance Criteria

- [x] A documented safety state machine covers startup, command timeout,
  hardware exception, invalid input, connection loss, graceful shutdown,
  process/container restart, Raspberry Pi restart, and recovery.
- [x] Movement commands have explicit, validated, bounded timeout behavior;
  an expired command cannot remain active indefinitely in the software path.
- [x] Every timeout/failure transition cancels or supersedes stale work and
  requests semantic `DroneCommand.Stop` through `IDroneHardware`.
- [x] STOP attempts have explicit timeout/retry limits; failure leaves the
  system faulted and blocks movement rather than falling back to mock.
- [x] Startup, recovery, and reconnection require a safe baseline before
  accepting movement commands.
- [x] Graceful shutdown attempts STOP through the common hardware abstraction
  and records success/failure without claiming physical confirmation.
- [x] Mock tests cover all software transitions, stale completion handling,
  timeout/cancellation, recovery, and bounded retry behavior without a Pi.
- [x] Real-provider, container-permission, GPIO, wiring, and abrupt-failure
  tests are explicitly identified as runtime/physical prerequisites and are not
  satisfied by mock tests or an ARM64 build.
- [x] Process crash, forced termination, power loss, and Pi restart are not
  claimed safe unless a separate external mechanism is documented and verified.
- [x] With `HARDWARE_MODE=real` and
  `Safety:ExternalAbruptFailureProtectionVerified=false`, startup safety remains
  faulted and no controller/hardware operation is issued; only an explicitly
  verified external mechanism may enable the setting.
- [x] Existing API/frontend contracts remain unchanged, and no safety behavior
  depends on frontend participation.
- [x] No GPIO levels, L298N mappings, PWM values, motor directions, or electrical
  limits are invented by this task.
