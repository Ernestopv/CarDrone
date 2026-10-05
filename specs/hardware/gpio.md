# GPIO Abstraction

## Purpose

Define the Infrastructure-only GPIO seam used by the Raspberry Pi hardware
provider. The seam separates semantic drone behavior from low-level digital
line access, supports a deterministic mock on development machines, and keeps
the real Linux/Raspberry implementation replaceable and testable.

This task defines GPIO access mechanics only. It does not assign physical
meaning to any configured pin or define motor behavior.

## Dependencies

- `specs/architecture/runtime-deployment.md` — D1/D3/D5, layer boundaries,
  runtime modes, verification levels, and the prerequisite ledger.
- `specs/hardware/hardware-abstraction.md` — `IDroneHardware` remains the
  Application-facing seam; GPIO must remain below it.
- `specs/hardware/raspberry-hardware-provider.md` — the existing provider and
  `HardwareDroneController` that will consume the GPIO implementation.
- `specs/docker/raspberry-hardware-access.md` — the Docker configuration
  boundary. Target-specific device paths, groups, and permissions are deferred;
  Task 25 must not hardcode or depend on them.
- `tasks/BACKLOG.md` Task 25 — configured GPIO values and the explicit ban on
  defining motor direction in this task.
- Task 26 fail-safe, Task 27 dry-run, Task 28 motor wiring, Task 29 motor
  direction, and Task 30 PWM specifications — downstream consumers and
  boundaries.
- Existing `DroneControl.Infrastructure` project conventions and xUnit test
  conventions in `backend/tests/DroneControl.Application.Tests`.

## Scope

- Define an Infrastructure-level `IGpioController` seam for digital GPIO line
  operations.
- Define the smallest value vocabulary needed to represent a configured line,
  output mode, and digital output level without assigning electrical or motor
  semantics.
- Bind the existing `GPIO` configuration section at the Infrastructure
  boundary. The named values `Pin1`, `Pin2`, `Pin3`, and `Pin4` are configuration
  identifiers only.
- Provide a deterministic `MockGpioController` for Windows/Linux development
  and unit tests.
- Provide a `RaspberryGpioController` adapter over a small isolated
  `IRaspberryGpioPlatform` boundary. The concrete Linux GPIO API/library is
  selected and implemented later; it is not required to complete Task 25.
- Validate configuration before any line is opened or written.
- Preserve cancellation, disposal, and error propagation contracts for real
  line operations.
- Keep the seam usable by `RaspberryDroneHardware` without moving GPIO
  knowledge into Domain, Application, API, or frontend code.

## Out of Scope

- Changing `IDroneHardware`, `IDroneController`, `DroneService`, API contracts,
  Domain types, or frontend code.
- Defining which configured pin maps to L298N `IN1`, `IN2`, `IN3`, `IN4`, `ENA`,
  or `ENB`.
- Defining `forward`, `backward`, `left`, `right`, or `stop` line patterns.
- Defining HIGH/LOW combinations as motor directions or selecting a physical
  STOP electrical state.
- PWM access, PWM frequency, duty-cycle conversion, enable-line behavior, or
  speed control. `PWM1` and `PWM2` remain reserved for Task 30.
- Fail-safe, watchdog, timeout, shutdown, or restart behavior.
- Dry-run logging/suppression behavior. Task 27 may wrap this seam later.
- Camera, uStreamer, Dockerfile, Compose, or new remote services.
- Selecting or implementing a concrete Raspberry Pi/Linux GPIO API/library,
  device path, GPIO chip identifier, kernel interface, OS-specific mechanism,
  ownership/group ID, container permission, or electrical capability. These
  remain `NOT VERIFIED` until later Pi inventory/runtime work.
- Claiming physical actuation or real-hardware confirmation.

## Architecture

```text
ASP.NET Core API
        ↓
Application: IDroneHardware
        ↓
Infrastructure: RaspberryDroneHardware
        ↓
Infrastructure: IGpioController
        ├── MockGpioController          (PC/tests, no hardware)
         └── RaspberryGpioController
                ↓
         IRaspberryGpioPlatform          (small isolated platform port)
                ↓
         concrete Linux/Pi implementation selected later
         (API, device path, and permissions: NOT VERIFIED)
```

`IGpioController`, `IRaspberryGpioPlatform`, and all GPIO
value/configuration types live in `DroneControl.Infrastructure`. The Application
layer must not reference them. `RaspberryGpioController` translates the generic
GPIO contract to the platform port; it does not itself reference a GPIO library,
Linux device path, or kernel API. A later concrete platform implementation is
the only code allowed to reference those details. The provider receives
`IGpioController` through dependency injection rather than constructing
hardware access itself.

Runtime selection remains at the composition root. `mock` selects a mock graph;
`real` selects the Raspberry adapter boundary after Task 23 platform selection;
its concrete Linux implementation is selected later. Task 27 owns any `dry-run`
wrapper; this task does not add mode conditionals to business logic.

## Domain Model

The low-level vocabulary is intentionally generic:

- `GpioPinConfiguration`: named configured line identifiers for `Pin1` through
  `Pin4`. It must not contain L298N roles or command names.
- `GpioPinMode`: only the modes required by this task's digital output seam;
  input support must not be added without a consumer requirement.
- `GpioPinValue`: `Low` and `High` as electrical line values, without claiming
  what either value does to a motor.
- `IGpioController`: the Infrastructure contract for configuring and writing a
  digital line.

The known configuration remains:

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

Task 25 reads and validates the four `Pin*` identifiers only. It must preserve
`PWM1` and `PWM2` as unconsumed configuration for Task 30 and must not infer
their role.

## Behavior

### Configuration

- Configuration is read once when the Infrastructure graph is composed.
- Required `Pin1`–`Pin4` values must be present, parseable as line identifiers,
  and valid for the selected GPIO mechanism.
- Duplicate configured identifiers are rejected unless the verified target
  interface explicitly permits sharing and a later specification approves it.
- Missing, malformed, negative, or unsupported identifiers fail before any
  physical line is opened.
- Configuration errors do not fall back to mock or silently use defaults.
- The numeric values remain identifiers only; validation must not assign them
  semantic names or motor roles.

### Mock implementation

- `MockGpioController` performs no operating-system or hardware calls.
- It records configured lines and the latest requested value for test
  inspection.
- It validates the same input contract as the real adapter.
- It is deterministic, static-free, and does not add delays.
- It can be used on a PC regardless of the presence of a Raspberry Pi.

### Raspberry implementation

- `RaspberryGpioController` delegates through `IRaspberryGpioPlatform`; the
  concrete Raspberry/Linux API is intentionally selected later.
- It configures and writes only lines supplied through validated configuration.
- It does not open PWM interfaces or camera devices.
- It propagates unavailable-device, permission, invalid-line, and I/O failures
  to the existing Infrastructure/provider error boundary; it must not report a
  successful write when the underlying operation failed.
- It propagates platform failures and releases owned platform resources
  deterministically. The eventual platform implementation honors cancellation
  where its selected API supports it.

## Deferred Raspberry Pi GPIO Integration

Selection and verification of the concrete Raspberry Pi/Linux GPIO API is
intentionally deferred. Task 25 defines and tests the platform boundary, not a
specific library or device-access mechanism.

The following may remain `NOT VERIFIED` when Task 25 is completed:

- concrete Linux GPIO API/library and its ARM64 runtime compatibility;
- `/dev/gpiochip` or any other target-specific device path;
- GPIO chip/line identifiers exposed by the target kernel;
- Raspberry Pi OS-specific interface details;
- host ownership, group IDs, and container permissions;
- opening or writing a physical GPIO line;
- physical GPIO behavior.

Deferred Raspberry Pi runtime and physical GPIO validation do not block Task 25
completion. They are validated during later Raspberry Pi runtime/end-to-end
tasks. Missing target facts must never be replaced with guessed values.

### Digital values

`Low` and `High` are transport-level line values only. This specification does
not say whether either value means stopped, forward, backward, enabled, or
disabled for the connected hardware.

## Interfaces

The implementation should provide an Infrastructure contract equivalent in
responsibility to:

```csharp
public interface IGpioController : IDisposable
{
    void ConfigureOutput(int pin);

    void Write(int pin, GpioPinValue value);
}
```

The exact naming may follow existing project conventions, but the contract
must remain limited to configuration and digital output. If the selected
platform API requires asynchronous operations, the interface may use
`Task`/`ValueTask` with `CancellationToken`; synchronous blocking wrappers and
`.Result`/`.Wait()` usage are not permitted.

The Raspberry adapter delegates through an isolated platform port with
equivalent responsibilities, for example:

```csharp
public interface IRaspberryGpioPlatform : IDisposable
{
    void ConfigureOutput(int lineIdentifier);

    void Write(int lineIdentifier, GpioPinValue value);
}
```

`RaspberryGpioController` validates against `GpioPinConfiguration`, tracks
which configured lines have been set as outputs, and forwards the numeric
identifier and `Low`/`High` value unchanged through this port. It must not
interpret or transform them. The concrete Linux implementation of
`IRaspberryGpioPlatform` is deferred until a later task selects a GPIO API from
target evidence; a hand-written fake platform is sufficient to test the
adapter in Task 25.

Required implementation boundaries:

- `MockGpioController` — no hardware dependencies.
- `RaspberryGpioController` — translates to `IRaspberryGpioPlatform`, with no
  motor or command mapping. Its platform port is fakeable in Task 25 tests.
- `IRaspberryGpioPlatform` — the small Infrastructure port implemented by the
  concrete Linux GPIO integration in a later task after the target interface is
  selected and verified.
- Configuration binding/validation — Infrastructure composition code, not
  Domain or Application.

No GPIO interface is added to the HTTP wire or exposed to React.

## Validation

- Validate every configured identifier before opening or configuring a line.
- Validate that the four named digital pins are present and do not silently
  substitute the documented example values when configuration is absent.
- Reject duplicate or mechanism-incompatible identifiers.
- Reject attempts to write a line that was not configured as an output.
- Reject invalid enum/value inputs through the type system or explicit guards.
- Ensure cancellation and disposal do not leave a successful result reported
  after an interrupted or failed operation.
- Confirm `RaspberryGpioController` forwards identifiers and values through
  `IRaspberryGpioPlatform`; it must contain no concrete device path, API/library
  selection, or permission logic.
- Keep Application vocabulary-purity intact: no GPIO, pin, PWM, L298N, or
  device-path types may be introduced above Infrastructure.

## Error Cases

- Missing GPIO configuration → startup/configuration error.
- Malformed or unsupported line identifier → validation error before hardware
  access.
- Duplicate line identifiers → validation error unless explicitly supported by
  verified platform documentation.
- Unconfigured line write → deterministic argument/configuration error.
- Permission denied, missing device, unavailable GPIO interface, or I/O failure
  → explicit provider/infrastructure failure; never a successful no-op.
- Cancellation → standard `OperationCanceledException` behavior where the
  underlying API supports cancellation.
- Disposal followed by use → deterministic object-state failure; no hardware
  operation is attempted.
- `mock` mode must not produce any of the real-provider hardware failures.

## Platform Requirements

- Development PC: mock implementation works on Windows/Linux without GPIO
  devices or Raspberry-specific packages.
- Raspberry runtime: the eventual concrete platform implementation must target
  the verified `linux/arm64` GPIO interface and permissions. Selection of that
  interface is deferred and does not block this abstraction task.
- The Infrastructure project remains compatible with the existing .NET 10
  solution and current non-root container model.
- No concrete external GPIO package is selected or added in Task 25. A later
  platform-integration task must justify any package against the discovered
  target API, ARM64 support, licensing, and testability.
- GPIO numbers `23`, `24`, `21`, `20` are configuration examples/known values,
  not proof of line availability or physical wiring.

## Verification Boundary

The implementation must report evidence by level. A passing mock test must
never be presented as proof that the Raspberry Pi provider can access a real
GPIO line.

### Verifiable without a Raspberry Pi

The following are valid `mock-tested` or `built` results and may run on the
development PC:

- `IGpioController` compiles and remains isolated in Infrastructure.
- GPIO configuration parsing and validation work for valid, missing, malformed,
  negative, duplicate, and unsupported identifiers.
- `Pin1`–`Pin4` are read from configuration without hardcoded fallbacks.
- `PWM1` and `PWM2` remain unconsumed by Task 25.
- `MockGpioController` records configuration and writes deterministically.
- Invalid or unconfigured writes are rejected before an operation is recorded.
- Cancellation, disposal, and repeated mock operations have deterministic
  behavior.
- A fake platform adapter proves that `RaspberryGpioController` forwards the
  configured identifier and `Low`/`High` value, propagates failures, and does
  not add motor semantics.
- Existing backend tests and the ARM64 image build remain green.

These tests prove software contracts only. They do not prove device paths,
kernel support, permissions, line availability, electrical output, or motor
behavior.

### Deferred Raspberry Pi integration and runtime

The following are intentionally deferred; they are not acceptance blockers for
Task 25 and cannot be accepted from mock tests alone:

- Selection of the actual GPIO library/API and its ARM64 runtime compatibility.
- Confirmation that the configured line identifiers correspond to usable lines
  on the target Pi's actual GPIO interface.
- Confirmation of the actual device/interface paths discovered by Task 24.
- Confirmation that the Docker container exposes the required interface.
- Confirmation that the non-root container user has the minimum required
  permissions.
- Opening, configuring, writing, and releasing a real GPIO line on the target
  Raspberry Pi.
- Behavior when the real device is missing, busy, inaccessible, or reports an
  I/O failure.

These results must be labeled `Pi-runtime-verified` only after execution on the
target runtime. An ARM64 image build alone is only `built`. Task 25 may complete
at the `built`/`mock-tested` level with these Pi-specific results recorded as
`NOT VERIFIED`.

### Requires separate physical verification

The following remain outside this task even if a real GPIO write succeeds:

- Which line drives an L298N input or enable pin.
- Whether `Low` or `High` produces a desired motor state.
- Forward/backward/left/right/stop behavior.
- PWM output, speed response, electrical limits, or fail-safe behavior.

Those claims require the owning Tasks 26–30 and the relevant
`real-hardware-verified` evidence.

## Security / Safety

- GPIO access is confined to Infrastructure and the configuration boundary
  established by Task 24; concrete device exposure remains deferred.
- Least-privilege device and permission requirements remain mandatory.
- This task must not use `privileged: true`, host-wide device access, or host
  filesystem mounts as an access shortcut.
- No operation may claim physical confirmation merely because the adapter
  accepted a `High` or `Low` write; `Applied` semantics remain software-level.
- No startup, shutdown, exception, timeout, or process-loss safety state is
  defined here; Task 26 owns those requirements.
- No motor can move based on this abstraction alone because no semantic command
  mapping is included.

## Testing Scenarios

1. Inspect the change set: GPIO types and implementations remain in
   Infrastructure; Domain/Application/API/frontend and existing contracts are
   unchanged.
2. Configuration tests cover valid four-pin configuration, missing values,
   malformed values, negative values, duplicate identifiers, and preservation
   of unconsumed `PWM1`/`PWM2` configuration.
3. Mock tests cover configure-before-write, repeated writes, invalid/unconfigured
   writes, deterministic state inspection, cancellation contract, and disposal.
4. Adapter tests use a hand-written fake platform API to verify that the real
   `RaspberryGpioController` passes the configured identifier and digital value
   unchanged through `IRaspberryGpioPlatform`, does not assign motor meaning,
   propagates failures, and releases resources. No concrete Raspberry/Linux
   API is needed for these Task 25 tests.
5. Regression tests prove the existing mock drone path and HTTP wire are
   unchanged; no GPIO operation is reachable from the PC mock graph.
6. Build and test the backend on the existing development toolchain twice
   consecutively; no physical hardware claim is made.
7. Raspberry Pi inventory/runtime verification is deferred. If a Pi is
   available, opening/configuring/writing a real line is a later runtime check;
   record it as `Pi-runtime-verified`, not `real-hardware-verified`, unless
   physical behavior has separately been observed and documented.
8. Confirm no motor direction, L298N wiring, PWM, fail-safe, or dry-run behavior
   was tested or claimed by this task.

## Acceptance Criteria

- [x] `IGpioController`, `IRaspberryGpioPlatform`, and their low-level
  configuration/value types exist only in Infrastructure and expose only
  configured digital output operations.
- [x] `MockGpioController` runs without Raspberry Pi hardware, external GPIO
  calls, or static shared state, and records operations for deterministic tests.
- [x] `RaspberryGpioController` exists and delegates through the isolated
  `IRaspberryGpioPlatform` boundary; it does not contain motor-command, L298N,
  PWM, camera, concrete device-path, or permission logic.
- [x] `RaspberryGpioController` is tested with a fake platform implementation
  that proves configured identifiers and `Low`/`High` values are forwarded
  unchanged, failures propagate, and resources are disposed.
- [x] `GPIO:Pin1` through `GPIO:Pin4` are read from configuration and validated;
  no pin number is hardcoded as a fallback or assigned a physical role.
- [x] `GPIO:PWM1` and `GPIO:PWM2` remain unconsumed by Task 25 and are reserved
  for Task 30.
- [x] Missing, malformed, duplicate, unsupported (when a verified capability
  set is available), or unconfigured line values fail explicitly before a
  physical operation; no silent fallback occurs.
- [x] No unverified device path, GPIO API/library, group ID, or permission is
  hardcoded. The concrete Raspberry Pi/Linux GPIO API may remain `NOT VERIFIED`.
- [x] Real-adapter permission, missing-device, I/O, cancellation, and disposal
  failures are propagated/tested at the platform abstraction boundary and are
  never reported as successful writes.
- [x] The Application layer remains free of GPIO/pin/PWM/L298N/device-path
  dependencies, and the existing `IDroneHardware`/HTTP/frontend contracts are
  unchanged.
- [x] Mock mode works on the development PC without Raspberry devices. The
  Raspberry adapter delegates only through `IRaspberryGpioPlatform`; a concrete
  Pi implementation is selected later and is not required for Task 25.
- [x] Backend build and tests pass with no physical-hardware claim; the ARM64
  image build passes. Any Raspberry validation is labeled according to the
  verification-level vocabulary.
- [x] The implementation report separates `mock-tested` software results from
  inventory-dependent `Pi-runtime-verified` results; no mock test is described
  as proving real device access or permissions.
- [x] Deferred Raspberry Pi validation does not block Task 25 completion. The
  concrete Linux API/library, device paths, chip identifiers, Raspberry Pi OS
  interface details, group IDs/permissions, opening/writing real lines, and
  physical GPIO behavior may all remain `NOT VERIFIED`.
- [x] No motor direction, wiring, PWM envelope, dry-run, or fail-safe behavior
  is implemented or accepted as part of Task 25.
