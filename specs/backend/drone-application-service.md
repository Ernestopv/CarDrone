# Drone Application Service

## Purpose

Create the application-level service abstraction that sits between the API and the drone controller.

This task defines the contracts (`IDroneService`, `IDroneController`) and the orchestration implementation (`DroneService`), designed for constructor injection. The container registration is deliberately deferred to Task 11, which registers the controller implementation and the service together: the Development environment validates the whole dependency graph eagerly at startup, so an incomplete registration would prevent the API from starting. The controller contract is the seam that Task 11 fills with a simulated implementation and that Task 12 consumes through endpoints.

This task adds no endpoints, no controller implementation, and no hardware behavior.

---

## Dependencies

- `specs/backend/domain-model.md` — `DroneCommand`, `DroneState`, `DroneStatus`, `ConnectionStatus`, speed invariant, and the guidance that reporting layers set `Api = Connected` when serving status.
- `specs/backend/backend-setup.md` — project structure, existing dependency-injection container, dependency direction (`Api → Application → Domain`).
- `tasks/BACKLOG.md` — Task 10 capabilities and requirements.
- `specs/frontend/drone-simulator.md` and `specs/frontend/error-states.md` — the established `DroneService` contract shape and failure semantics the backend mirrors.
- `MEMORY.md` — semantic commands; requested vs confirmed command distinction.

---

## Scope

- `IDroneService` contract in `DroneControl.Application` with capabilities: `GetState`, `Connect`, `Disconnect`, `SendCommand`, `SetSpeed`.
- `IDroneController` contract in `DroneControl.Application` — the downstream "drone abstraction" from the backlog architecture.
- `DroneService` implementation of `IDroneService` in `DroneControl.Application`.
- Runtime verification that the API still starts and `/api/health` still responds (`Program.cs` stays untouched; the DI registration is deferred to Task 11 — see Out of Scope).

---

## Out of Scope

- `IDroneController` implementation (`MockDroneController`) and its registration — Task 11.
- REST endpoints, controllers, request/response DTOs — Task 12.
- Mapping failures to HTTP status codes, error middleware, logging policy — Tasks 12 and 16.
- Connection-state rules such as rejecting commands while disconnected — detection belongs to the controller implementation (Task 11); error surfacing belongs to Tasks 12/16.
- Requested/confirmed command tracking — owned by the controller simulation (Task 11); `DroneService` never modifies `ConfirmedCommand`.
- Test projects — Task 17.
- Docker — Task 13.
- GPIO, PWM, pin numbers, hardware libraries — never in the Application layer.
- Any frontend change.
- The `IDroneService` → `DroneService` container registration and the `IDroneController` registration — both land together in Task 11.

**Container boundary:** ASP.NET Core validates every service descriptor eagerly at host startup in the Development environment (`ValidateOnBuild`). Registering `DroneService` before an `IDroneController` implementation exists crashes the API on start, so the dependency graph must be completed atomically: Task 11 registers the controller implementation and `IDroneService` → `DroneService` (scoped) in the same change. This task registers nothing; the API must start and serve `/api/health`.

---

## Architecture

```text
DroneControl.Api  (Program.cs: DI registration only)
        ↓  IDroneService
DroneControl.Application
        │   DroneService (orchestration)
        ↓  IDroneController
DroneControl.Application  (contract only; implementation arrives in Task 11)
        ↓
MockDroneController  (Task 11 — not this task)
```

Rules:

- `DroneControl.Application` keeps its single `ProjectReference` to `DroneControl.Domain`. No csproj change is required or allowed.
- The Application layer uses no ASP.NET Core types, no serialization, and no hardware identifiers.
- Controllers remain logic-free; this task adds nothing at all to `DroneControl.Api` — `Program.cs` stays unchanged (verified as an acceptance criterion).

---

## Interfaces

Signatures below are contract notation, not implementation code. All types live in namespace `DroneControl.Application`.

### IDroneService

The application-facing contract (the backlog's expected capability):

```text
IDroneService
├── GetStateAsync(CancellationToken = default)               → Task<DroneStatus>
├── ConnectAsync(CancellationToken = default)                → Task
├── DisconnectAsync(CancellationToken = default)             → Task
├── SendCommandAsync(DroneCommand, CancellationToken = default) → Task
└── SetSpeedAsync(int, CancellationToken = default)          → Task
```

- All operations are asynchronous, use the `Async` suffix, and accept an optional `CancellationToken`.
- `GetStateAsync` returns `DroneStatus` — the client-facing status shape fixed by `domain-model.md`.
- Commands are semantic `DroneCommand` values; no hardware meaning crosses this boundary.

### IDroneController

The downstream drone abstraction that `DroneService` depends on:

```text
IDroneController
├── GetStatusAsync(CancellationToken = default)               → Task<DroneStatus>
├── ConnectAsync(CancellationToken = default)                → Task
├── DisconnectAsync(CancellationToken = default)             → Task
├── SendCommandAsync(DroneCommand, CancellationToken = default) → Task
└── SetSpeedAsync(int, CancellationToken = default)          → Task
```

- The controller fills `State` (including connection, camera, requested/confirmed command, speed) and `RaspberryPi` in the returned `DroneStatus`.
- The controller does **not** own the `Api` field; its value is irrelevant to callers because `DroneService` always overwrites it.

### DroneService

- Implements `IDroneService`.
- Receives `IDroneController` through constructor injection — its only constructor dependency.
- Stateless apart from that dependency: no static mutable state, no service-locator access, no ambient context.

### DI Registration (deferred to Task 11)

- `DroneService` is constructed exclusively through constructor injection, so it can be registered as `IDroneService` → `DroneService` (scoped) the moment `IDroneController` has an implementation.
- This task adds no registration: the Development host validates the full dependency graph at `Build()` (see Out of Scope), so the service and its controller must be registered together.
- Task 11 registers `IDroneController` → `MockDroneController` and `IDroneService` → `DroneService` in the same change.

---

## Behavior

### GetStateAsync

1. Call `IDroneController.GetStatusAsync`.
2. Return the controller's `DroneStatus` with `Api` overwritten to `ConnectionStatus.Connected` (the Application layer is serving the response, so the API component is up by definition).
3. `State` and `RaspberryPi` pass through from the controller unchanged.

### ConnectAsync / DisconnectAsync

Delegate to the corresponding `IDroneController` operation and await it. `DroneService` holds no connection state of its own.

### SendCommandAsync

Delegate the exact `DroneCommand` value to the controller. The controller records the requested command and the confirmed command after acknowledgement; `DroneService` does not duplicate or modify that tracking.

### SetSpeedAsync

1. Validate `0 ≤ speed ≤ 100` at the application boundary. Values outside the range throw `ArgumentOutOfRangeException` **before** any delegation.
2. Otherwise delegate the identical value to the controller.
3. Never clamp and never silently alter the value. The domain invariant inside the controller's state objects remains the second, independent enforcement layer.

### Exception Transparency

Exceptions raised by the controller propagate unchanged — same type, same instance. `DroneService` never wraps, swallows, or translates failures, so no caller can observe success unless the controller completed the operation. Success reporting without confirmation stays impossible by construction.

### Async Rules

- All five operations are awaited; no blocking on `.Result` / `.Wait()`.
- With a healthy controller, valid inputs produce no exceptions.

---

## Validation

How the acceptance criteria are verified for this task:

- `dotnet build` on the solution (0 errors, 0 warnings).
- A temporary throwaway harness (outside the repository, deleted after validation) containing a fake `IDroneController`, verifying delegation, `Api` stamping, speed rejection, exception transparency, and constructor shape via reflection.
- Source inspection for forbidden dependencies and scope guards (no ASP.NET Core types, no GPIO, no controller implementation, no new endpoints).
- Runtime verification: start the API and confirm `GET /api/health` returns HTTP 200 with `{"status":"ok"}` with `Program.cs` unchanged.
- `dotnet test` (no backend test projects exist yet; durable tests belong to Task 17, `specs/backend/backend-tests.md`).

---

## Error Cases

- `SetSpeedAsync` with a value outside `0-100` → `ArgumentOutOfRangeException` thrown by `DroneService`; the controller is not called.
- Any controller failure → propagates unchanged through `DroneService`.
- Commands while disconnected, unavailable controller implementations, and unexpected server errors are **not** handled by this task; they are defined by Tasks 11, 12, and 16.
- Valid input with a healthy controller → no exceptions.

---

## Platform Requirements

- `net10.0` class library code (`DroneControl.Application`) plus existing ASP.NET Core host code (`DroneControl.Api`); builds and runs on Windows and Linux.
- Pure managed code; no operating-system-specific or native APIs.

---

## Security / Safety

- The Application layer references only the Domain — no pin numbers, no hardware identifiers, no way to leak wiring details upward.
- Semantic commands pass through unchanged; no command-to-hardware mapping exists in this layer.
- No false success: because exceptions propagate untouched and confirmation tracking is controller-owned, no layer can report a command as executed without the controller's acknowledgement — preserving the requested vs confirmed safety distinction for future hardware phases.
- Invalid speed is rejected, not clamped, at the application boundary.

---

## Testing Scenarios

Automated tests are deferred to Task 17. Required behavior for these scenarios:

1. `DroneService` constructor accepts exactly `IDroneController`.
2. `GetStateAsync` returns controller `State` and `RaspberryPi` unchanged with `Api = Connected`, even when the controller returned a different `Api` value.
3. `SetSpeedAsync(-1)` and `SetSpeedAsync(101)` throw `ArgumentOutOfRangeException` and the fake controller records zero calls.
4. `SetSpeedAsync(0)` and `SetSpeedAsync(100)` reach the controller with identical values.
5. Each of the five `DroneCommand` values is forwarded exactly by `SendCommandAsync`.
6. `ConnectAsync` and `DisconnectAsync` call the matching controller operations.
7. An exception thrown by the fake controller surfaces as the same type and same instance.
8. `IDroneService` resolves from the container only after Task 11 registers both `IDroneController` and `IDroneService`.
9. Application source contains no ASP.NET Core usage, no static mutable state, and no GPIO identifiers.

---

## Acceptance Criteria

- [x] `DroneControl.Application` defines `IDroneService` with exactly five operations: `GetStateAsync`, `ConnectAsync`, `DisconnectAsync`, `SendCommandAsync`, `SetSpeedAsync`.
- [x] All `IDroneService` operations return `Task`, use the `Async` suffix, and accept an optional `CancellationToken`.
- [x] `GetStateAsync` returns `Task<DroneStatus>`; `SendCommandAsync` accepts `DroneCommand`; `SetSpeedAsync` accepts `int`.
- [x] `DroneControl.Application` defines `IDroneController` with exactly five operations: `GetStatusAsync`, `ConnectAsync`, `DisconnectAsync`, `SendCommandAsync`, `SetSpeedAsync`, typed with Domain types only.
- [x] `DroneService` implements `IDroneService` and its only constructor dependency is `IDroneController`.
- [x] `DroneService` has no static mutable state and performs no service-locator access.
- [x] `GetStateAsync` returns the controller's `State` and `RaspberryPi` unchanged with `Api = Connected`, regardless of the controller's reported `Api` value.
- [x] `SetSpeedAsync(-1)` and `SetSpeedAsync(101)` throw `ArgumentOutOfRangeException` without invoking the controller.
- [x] `SetSpeedAsync(0)` and `SetSpeedAsync(100)` delegate the identical value to the controller.
- [x] `SendCommandAsync` forwards the exact `DroneCommand` value to the controller.
- [x] `ConnectAsync` and `DisconnectAsync` delegate to the matching controller operations.
- [x] A controller exception propagates unchanged (same type, same instance) through `DroneService`.
- [x] `DroneControl.Api/Program.cs` is unchanged: no DI registration is added by this task (deferred to Task 11).
- [x] The API starts successfully and `GET /api/health` returns HTTP 200 with `{"status":"ok"}`.
- [x] `DroneControl.Api` still exposes only `GET /api/health`; no new endpoint exists.
- [x] No implementation of `IDroneController` exists anywhere in the solution.
- [x] `DroneControl.Application.csproj` is unchanged: only the `ProjectReference` to `DroneControl.Domain`, no `PackageReference`.
- [x] Application source contains no ASP.NET Core types, usings, or attributes, and no GPIO or pin identifiers.
- [x] `dotnet build backend/DroneControl.sln` succeeds with 0 errors and 0 warnings.
- [x] No new project is added to the solution; the solution still contains exactly the four original projects.
- [x] No frontend file is modified.
