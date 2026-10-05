# Backend Domain Model

## Purpose

Define the core drone domain types for the backend.

The types created by this task become the shared vocabulary for the Application and API layers built in later tasks.

This task is data modeling only: types, invariants, and documented semantics. No services, no endpoints, no hardware.

---

## Dependencies

- `specs/backend/backend-setup.md` — `DroneControl.Domain` exists and must remain independent.
- `tasks/BACKLOG.md` — Task 9 required types and requirements.
- `docs/PRD.md` — §9 Command Model, §10 Speed Control, §20 Frontend Domain Model.
- `frontend/src/types/drone.ts` and `specs/frontend/drone-simulator.md` — the established contract the backend model aligns with.
- `MEMORY.md` — semantic commands; requested vs confirmed command distinction.

---

## Scope

- Domain types inside `DroneControl.Domain`:
  - `DroneCommand`
  - `ConnectionStatus`
  - `CameraStatus`
  - `DroneState`
  - `DroneStatus`
- Speed range validation (`0-100`).
- Documented initial state values.
- Documented wire-mapping boundary for the later API task.

---

## Out of Scope

- Services or service interfaces (`IDroneService`) — Task 10.
- Simulator behavior — Task 11.
- REST endpoints, controllers, response DTOs, serialization configuration — Task 12.
- API error responses and error-handling middleware — Task 16.
- Test projects — Task 17.
- Docker — Task 13.
- GPIO, PWM, pin numbers, Raspberry Pi libraries — never in Domain.
- Command-failure tracking (`failedCommand`) — this stays frontend UI state; later tasks report command failures to callers as errors.
- Any frontend change.

---

## Domain Model

All types live in namespace `DroneControl.Domain` inside the existing `DroneControl.Domain` project. The project keeps zero package references and zero project references.

### DroneCommand

Enum with exactly these values:

```text
Forward
Backward
Left
Right
Stop
```

Commands are semantic only. No pin mapping exists in the Domain.

### ConnectionStatus

Enum with exactly these values:

```text
Offline
Connecting
Connected
Error
```

### CameraStatus

Enum with exactly these values:

```text
Offline
Connecting
Streaming
Error
```

### DroneState

Immutable record describing the drone subsystem:

```text
DroneState
├── Connection       : ConnectionStatus
├── Camera           : CameraStatus
├── RequestedCommand : DroneCommand
├── ConfirmedCommand : DroneCommand
└── Speed            : int (0-100, percent)
```

The speed percentage to PWM relationship is defined later; the Domain stores only the percentage.

Initial values:

```text
Connection       = Offline
Camera           = Offline
RequestedCommand = Stop
ConfirmedCommand = Stop
Speed            = 0
```

### DroneStatus

Immutable record describing the system-level status reported to upper layers:

```text
DroneStatus
├── State       : DroneState
├── RaspberryPi : ConnectionStatus
└── Api         : ConnectionStatus
```

Initial values:

```text
State       = initial DroneState
RaspberryPi = Offline
Api         = Offline
```

The Domain does not assume anything about hosting. Reporting layers (Application/API) set `Api = Connected` when they serve status responses.

---

## Behavior

### Immutability

State changes produce new instances (record `with` expressions). The records expose no public mutable setters.

### Speed Invariant

`Speed` must always be within `0-100` inclusive.

- Out-of-range values throw `ArgumentOutOfRangeException`.
- The rule applies at construction and on record `with` updates; validation must sit in an initialization path that every construction route passes through, so updates cannot bypass it.
- The Domain never silently clamps. The frontend keeps its existing client-side clamping behavior unchanged.

### Command Lifecycle (semantics for later tasks; enforcement belongs to Tasks 10-11)

```text
command requested  → RequestedCommand updated
service confirms   → ConfirmedCommand updated
```

- `RequestedCommand` records user intent.
- `ConfirmedCommand` changes only on explicit confirmation.
- The Domain performs no confirmation itself.
- A command must never appear confirmed without explicit confirmation.

### Connection Flow (enforcement belongs to services)

```text
offline → connecting → connected
connected → offline
connecting → error
```

### Camera Flow (aligned with the frontend simulator specification)

```text
drone disconnected  → camera offline
drone connected     → camera may reach streaming
```

### STOP

`Stop` is a first-class command value. No separate emergency-stop concept exists in the model.

---

## Interfaces

This task defines type contracts only. The consumers are built in later tasks:

```text
DroneState / DroneStatus
        ↓
Application services (Task 10)
        ↓
API responses (Task 12)
```

Status reporting to clients uses `DroneStatus`; `DroneState` is the nested drone snapshot inside it.

### Wire Mapping Boundary

Wire format configuration belongs to the API layer (Task 12 / integration tasks), never to the Domain. The canonical value mapping is:

| Domain member | JSON value |
|---|---|
| `Forward` | `"forward"` |
| `Backward` | `"backward"` |
| `Left` | `"left"` |
| `Right` | `"right"` |
| `Stop` | `"stop"` |
| `Offline` | `"offline"` |
| `Connecting` | `"connecting"` |
| `Connected` | `"connected"` |
| `Streaming` | `"streaming"` |
| `Error` | `"error"` |

Rules:

- Domain member names are PascalCase C# identifiers.
- The Domain contains no serialization attributes; the API layer maps enum values to lowercase strings.
- API property names use the ASP.NET Core camelCase default, so `RaspberryPi` reaches clients as `raspberryPi`.
- GPIO or pin information never appears in any contract.

---

## Validation

Validation rules introduced by this task:

1. `Speed` must be within `0-100` inclusive; violations throw `ArgumentOutOfRangeException`.
2. Commands and statuses are limited to their enum values; strong typing makes invalid values unrepresentable in C# (string parsing belongs to the API layer).
3. The Domain project remains free of all package and project references.

How the criteria are verified for this task:

- `dotnet build` on the solution.
- Source inspection for forbidden dependencies, attributes, and hardware identifiers.
- Runtime checks through a temporary throwaway verification harness, deleted after validation.
- Durable automated tests belong to Task 17 (`specs/backend/backend-tests.md`).

---

## Error Cases

- `Speed < 0` or `Speed > 100` → `ArgumentOutOfRangeException`, both at construction and on `with` updates.
- Invalid command or status strings cannot exist in the Domain; parsing and rejecting them is the API layer's responsibility (Tasks 12 and 16).
- No valid input causes an exception.

---

## Platform Requirements

- `net10.0` class library; builds and runs on Windows and Linux.
- Pure managed code; no operating-system-specific or native APIs.

---

## Security / Safety

- No GPIO pin numbers or hardware identifiers anywhere in the Domain, so no upper layer can leak wiring details through domain types.
- The requested vs confirmed separation is part of the model, so no layer can present a command as executed without explicit confirmation. This is the foundation for future hardware safety behavior.
- `Stop` is always representable as a command value.
- No secrets, environment-specific values, or configuration live in the Domain.

---

## Testing Scenarios

Automated tests are deferred to Task 17. The following scenarios define the required behavior:

1. Each enum contains exactly the specified values and no others.
2. Initial `DroneState` matches the documented initial values.
3. Initial `DroneStatus` matches the documented initial values.
4. `Speed = 0` and `Speed = 100` are accepted.
5. `Speed = -1` and `Speed = 101` throw `ArgumentOutOfRangeException` at construction.
6. An out-of-range `Speed` set through a record `with` expression throws `ArgumentOutOfRangeException`.
7. `DroneControl.Domain.csproj` has no `PackageReference` and no `ProjectReference`.
8. Domain source contains no ASP.NET Core usage, no serialization attributes, and no GPIO or pin constants.
9. The state records expose no public mutable setters.

---

## Acceptance Criteria

- [x] `DroneControl.Domain` defines `DroneCommand` with exactly `Forward`, `Backward`, `Left`, `Right`, `Stop`.
- [x] `DroneControl.Domain` defines `ConnectionStatus` with exactly `Offline`, `Connecting`, `Connected`, `Error`.
- [x] `DroneControl.Domain` defines `CameraStatus` with exactly `Offline`, `Connecting`, `Streaming`, `Error`.
- [x] `DroneState` exposes `Connection`, `Camera`, `RequestedCommand`, `ConfirmedCommand`, and `Speed`.
- [x] `DroneStatus` exposes `State` (`DroneState`), `RaspberryPi` (`ConnectionStatus`), and `Api` (`ConnectionStatus`).
- [x] `DroneState` and `DroneStatus` are records with init-only properties; no public mutable setters exist.
- [x] Initial `DroneState`: `Connection = Offline`, `Camera = Offline`, `RequestedCommand = Stop`, `ConfirmedCommand = Stop`, `Speed = 0`.
- [x] Initial `DroneStatus`: `State` equals the initial `DroneState`, `RaspberryPi = Offline`, `Api = Offline`.
- [x] Constructing `DroneState` with `Speed = -1` throws `ArgumentOutOfRangeException`.
- [x] Constructing `DroneState` with `Speed = 101` throws `ArgumentOutOfRangeException`.
- [x] Constructing `DroneState` with `Speed = 0` and `Speed = 100` succeeds.
- [x] Setting `Speed` outside `0-100` through a record `with` expression throws `ArgumentOutOfRangeException`.
- [x] `dotnet build backend/DroneControl.sln` succeeds with 0 errors and 0 warnings.
- [x] `DroneControl.Domain.csproj` has no `PackageReference` and no `ProjectReference`.
- [x] Domain source contains no ASP.NET Core types, usings, or attributes.
- [x] Domain source contains no GPIO or PWM pin constants or other hardware identifiers.
- [x] Domain source contains no serialization attributes or wire-format code.
- [x] No new project is added to the solution; `DroneControl.Api` still exposes only `GET /api/health`.
- [x] No frontend file is modified.
