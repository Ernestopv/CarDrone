# Backend Drone Simulator

## Purpose

Task 11 fills the `IDroneController` seam with a simulated implementation, `MockDroneController`, so the backend behaves like a drone without any hardware: connection lifecycle, requested vs confirmed commands, speed, camera state, and Raspberry Pi state are produced entirely in memory.

The task also closes the dependency-injection boundary deferred from Task 10 by registering `IDroneController` → `MockDroneController` and `IDroneService` → `DroneService` in the same change; the Development environment validates the whole graph eagerly at startup, so a successful start proves the graph is complete.

Simulation timings and offline rules mirror the Phase 1 frontend mock (`frontend/src/mocks/mockDroneService.ts`) so behavior stays consistent when the API is wired to the UI in Tasks 18–19.

## Dependencies

- `specs/backend/backend-setup.md` — project layout and responsibilities: Api holds wiring only; Application holds contracts; Infrastructure hosts implementations.
- `specs/backend/domain-model.md` — `DroneState`, `DroneStatus`, `ConnectionStatus`, `CameraStatus`, `DroneCommand`; initial state; the speed invariant.
- `specs/backend/drone-application-service.md` — `IDroneController`, `IDroneService`, `DroneService`; the deferred-registration boundary this task closes; the "detection belongs to Task 11" rule for commands while disconnected.
- Source contracts: `backend/src/DroneControl.Application/IDroneController.cs`, `IDroneService.cs`.
- Behavior parity reference (behavior only; no code reuse): `frontend/src/mocks/mockDroneService.ts` and `frontend/src/utils/status.ts` — 700 ms connection delay, 250 ms acknowledgement delay, connecting status shape (`connecting` drone + camera + services), connect-success state, offline rules.

## Scope

- `MockDroneController : IDroneController` in `DroneControl.Infrastructure`.
- Simulation of: connect, disconnect, requested command, confirmed command, speed, camera state, Raspberry Pi state.
- `DroneNotConnectedException` in `DroneControl.Application` — the detection contract for command-while-disconnected (surfacing belongs to Tasks 12/16).
- Behavior rules: simulated delays, state guards, cancellation, supersession of pending attempts, concurrent access.
- DI registrations in `DroneControl.Api/Program.cs` (both, atomically).
- Validation by build, runtime API start with `/api/health`, and a throwaway verification harness that is deleted after use.

## Out of Scope

- REST endpoints and HTTP status mapping — Task 12.
- Mapping `DroneNotConnectedException` / `ArgumentOutOfRangeException` to HTTP responses — Tasks 12/16.
- Test projects and committed tests — Task 17.
- Real hardware, GPIO, PWM, camera, uStreamer, or any network I/O — never in this layer.
- Failure injection: the mock never enters `ConnectionStatus.Error` or `CameraStatus.Error`; connection attempts always succeed after the delay. The frontend `ScenarioService` failure scenarios have no backend counterpart in this task.
- Configurable delays (appsettings) — named constants only.
- Frontend changes; Docker; SignalR.

## Architecture

```text
DroneControl.Api            Program.cs: AddSingleton<IDroneController, MockDroneController>
        |                         AddScoped<IDroneService, DroneService>
DroneControl.Application    IDroneService / IDroneController / DroneService / DroneNotConnectedException
        | implements
DroneControl.Infrastructure MockDroneController  (in-memory state; depends only on Application + Domain)
        |
DroneControl.Domain         DroneState / DroneStatus (value types only)
```

**Placement:** the controller implementation is an adapter behind the Application contract, so it lives in Infrastructure — where the future Raspberry Pi implementation will also live. Swapping the simulation for hardware later becomes a registration change, not a code move. `DroneControl.Infrastructure` already references Application and Domain, so no project-file changes are needed.

**Lifetime:** `MockDroneController` is registered as a **singleton** because it owns the simulated state; a scoped or transient registration would create a fresh drone per request and no connection would survive the next call. `DroneService` stays scoped and stateless (Task 10).

**Atomic registration:** `Program.cs` gains both mappings in one change. Registering `IDroneService` → `DroneService` alone crashes startup under Development `ValidateOnBuild` (observed in Task 10); registering both completes the graph, and a successful start positively proves the whole graph is constructible.

## Interfaces

### MockDroneController

- `public sealed class` in namespace `DroneControl.Infrastructure`.
- Implements all five members of `IDroneController`; adds no other public members.
- Constructed with no injected dependencies.
- Holds the simulated state as instance fields: the current `DroneState` and `ConnectionStatus RaspberryPi`. It never sets `DroneStatus.Api`.

### DroneNotConnectedException

- `public sealed class` in `DroneControl.Application`, deriving from `Exception`.
- Thrown when a command arrives while `Connection` is not `Connected`.
- Purpose: gives Tasks 12/16 a stable, specific type to map. `InvalidOperationException` is too generic — it could originate anywhere in the pipeline.

### DI Registration (Program.cs)

```csharp
builder.Services.AddSingleton<IDroneController, MockDroneController>();
builder.Services.AddScoped<IDroneService, DroneService>();
```

Both registrations land together in this task; nothing else in `Program.cs` changes.

## Behavior

### Initial state

`GetStatusAsync` returns `State` = initial `DroneState` (Offline, Offline, Stop, Stop, 0), `RaspberryPi` = Offline, `Api` = default (`Offline`; overwritten by `DroneService`).

### ConnectAsync

Only an `Offline` drone starts a connection attempt:

1. Before the first asynchronous suspension, state becomes `Connection = Connecting`, `Camera = Connecting`, `RaspberryPi = Connecting` (frontend parity with `createConnectingDroneStatus`).
2. The attempt waits the simulated connection delay (named constant, 700 ms).
3. On success: `Connection = Connected`, `Camera = Streaming`, `RaspberryPi = Connected`; commands and speed remain `Stop` / `Stop` / `0` (an attempt only starts from the initial state and nothing modifies commands or speed while connecting). `Api` untouched.
4. If cancelled during the delay: `OperationCanceledException` propagates and the state returns to fully initial — never left stuck in `Connecting`.
5. If `DisconnectAsync` ran during the delay, the attempt is superseded: it resolves normally without applying any state; the status stays fully initial.

Invoked while `Connecting` or `Connected`: resolves as a no-op, status unchanged. The frontend mock re-runs the transition instead; the backend guards it so a repeated connect cannot reset a live connection. Deliberate divergence.

### DisconnectAsync

- From any state, with no simulated delay: returns to fully initial status (`Connection = Offline`, `Camera = Offline`, `RaspberryPi = Offline`, commands `Stop`/`Stop`, speed `0`), `Api` untouched.
- It supersedes an in-flight connection attempt and any in-flight command acknowledgement: a pending confirmation must not be applied after the reset.

### SendCommandAsync

While `Connection = Connected`:

1. Before the first asynchronous suspension, `RequestedCommand` becomes the issued command; `ConfirmedCommand` is unchanged (Task 10 rule: a request never confirms).
2. The controller waits the simulated acknowledgement delay (named constant, 250 ms).
3. It then sets `ConfirmedCommand` to the issued command — only if the drone is still connected and the confirmation has not been superseded by a disconnect.

While `Offline` or `Connecting`: throws `DroneNotConnectedException` immediately (no delay); status unchanged. The frontend Phase 1 mock silently resolves instead; the backend formalizes the failure because Task 16 must handle "command while disconnected" and the error-states convention maps rejection to the frontend `failedCommand` path. Deliberate divergence.

Overlapping commands: confirmations are applied in request order and never concurrently; once all pending commands complete, `ConfirmedCommand` equals the most recently requested command.

Cancellation during the acknowledgement delay: `OperationCanceledException` propagates; `RequestedCommand` stays updated, `ConfirmedCommand` keeps its previous value.

### SetSpeedAsync

- While `Connected`: speed updates to the given value. The Domain validates the range; an out-of-range value throws `ArgumentOutOfRangeException` and leaves state unchanged — never clamped.
- While `Offline` or `Connecting`: resolves as a no-op, speed unchanged — frontend parity ("ignores speed changes while offline"). The connection gate runs before the range check, so a direct out-of-range call while disconnected is also a no-op; via `DroneService`, out-of-range values always throw first (Task 10 pre-validation).

### Camera and Raspberry Pi

- Camera: `Offline` initially → `Connecting` during an attempt → `Streaming` when connected → `Offline` after disconnect/cancel. The mock never produces `CameraStatus.Error`.
- Raspberry Pi: `Offline` initially → `Connecting` during an attempt → `Connected` when connected → `Offline` after disconnect/cancel.
- Connection: the mock only ever produces `Offline`, `Connecting`, `Connected`. `ConnectionStatus.Error` is reserved for future real implementations.

### Concurrency

- All state reads and mutations are thread-safe; the singleton is shared by concurrent requests.
- Status reads respond while a delay is in flight — no lock may be held across the simulated delays.

## Validation

State invariants (enforced by construction, checked by the verification harness):

1. While `Connection = Offline`: the status is exactly the initial state (camera Offline, Pi Offline, Stop, Stop, 0).
2. While `Connection = Connecting`: commands are `Stop`/`Stop` and speed `0`; camera and Pi are `Connecting`. (Connect only starts from the fully initial state, and neither commands nor speed change during an attempt.)
3. `ConfirmedCommand` changes only through acknowledgement or a full reset — never when a command is merely requested.
4. The mock yields `Connection` ∈ {Offline, Connecting, Connected} and `Camera` ∈ {Offline, Connecting, Streaming} — never `Error`.
5. Speed is always 0–100 (Domain-enforced).
6. `DroneStatus.Api` is never set by the controller.

Domain invariants remain the second line of defense: `MockDroneController` builds new state with record `with` expressions, so `DroneState.Speed` rejects out-of-range values even when the controller is called directly, skipping `DroneService`.

## Error Cases

| Case | Behavior |
| --- | --- |
| Command while Offline or Connecting | `DroneNotConnectedException`; status unchanged |
| Out-of-range speed while Connected | `ArgumentOutOfRangeException` (Domain); state unchanged |
| Out-of-range speed while disconnected | No-op resolve (connection gate first); via `DroneService` it still throws — Task 10 pre-validation |
| Cancel during connection delay | `OperationCanceledException`; status returns to fully initial |
| Cancel during acknowledgement delay | `OperationCanceledException`; requested updated, confirmed unchanged |
| Connect while Connecting or Connected | Not an error — no-op resolve |
| Disconnect from any state | Not an error — immediate reset |

HTTP mapping of these failures belongs to Tasks 12/16.

## Platform Requirements

- .NET 10; C# conventions of the existing projects (file-scoped namespaces, XML doc comments, nullable context as configured).
- No new NuGet packages; no `.csproj` changes; no new projects; classic `DroneControl.sln` unchanged (4 projects).
- The API must start in the Development environment: `ValidateOnBuild` constructs the full graph, so startup success proves both registrations and `MockDroneController` construction are valid.
- Simulated delays are named constants with the frontend values (700 ms connection, 250 ms acknowledgement), used as `Task.Delay(delay, cancellationToken)`.

## Security / Safety

- The mock contains no GPIO, pin number, PWM, device, or hardware APIs and performs no network I/O — in-memory state plus timer delays only.
- Acknowledgements are simulated: code comments must state that a confirmed command means the simulator confirmed it, never hardware confirmation. While this controller is active, no layer may claim physical execution; real confirmation arrives only with a hardware implementation in a later phase.
- Thread-safe state prevents a race from reporting a confirmed command that was never processed — a wrong "confirmed" is a safety-relevant lie in a control system.

## Testing Scenarios

Validated by a throwaway console harness (temporary project referencing the target csproj, deleted after validation — no test project is created; Task 17 owns tests):

1. Initial status equals the fully initial `DroneStatus`.
2. Connect: the immediate status read shows Connecting/Connecting/Connecting; after completion Connected/Streaming/Connected with commands Stop/Stop and speed 0; elapsed ≥ 700 ms.
3. Connect while connecting/connected: no-op, status unchanged.
4. Disconnect: immediate reset from any state, no artificial delay.
5. Disconnect during a pending connect: after the connect task completes, status is fully initial.
6. Command while connected: immediate read shows requested = command, confirmed unchanged; after completion confirmed = command; elapsed ≥ 250 ms.
7. Overlapping commands: after both complete, requested = confirmed = last issued.
8. Command while offline (and while connecting) → `DroneNotConnectedException`, status unchanged.
9. Speed while connected updates; speed while offline resolves unchanged; direct `SetSpeedAsync(150)` while connected throws `ArgumentOutOfRangeException` with speed unchanged.
10. Cancellation during the connection delay → `OperationCanceledException`, status fully initial.
11. State persistence: two `DroneService` instances over one `MockDroneController` observe the same state (the simulated connection survives separate service instances).
12. Runtime: `dotnet build` 0 warnings / 0 errors; API starts (Development) and `GET /api/health` → 200 `{"status":"ok"}` — startup itself validates the completed DI graph.

## Acceptance Criteria

- [x] `MockDroneController` exists in `DroneControl.Infrastructure`, is `public sealed`, implements `IDroneController`, and constructs with no injected dependencies.
- [x] No `.csproj` changes: Infrastructure references only Application and Domain; no new packages, projects, or solution entries.
- [x] Initial `GetStatusAsync` returns `State` = initial `DroneState` (Offline, Offline, Stop, Stop, 0), `RaspberryPi` = Offline, `Api` = default (Offline).
- [x] Invoking `ConnectAsync` from Offline makes the immediately following status read report `Connection = Connecting`, `Camera = Connecting`, `RaspberryPi = Connecting` (transition applied before the first asynchronous suspension).
- [x] A completed connection takes at least the 700 ms simulated delay and then reports `Connected` / `Streaming` / `Connected` with `RequestedCommand = Stop`, `ConfirmedCommand = Stop`, `Speed = 0`.
- [x] `ConnectAsync` invoked while Connecting or Connected resolves without changing the status.
- [x] `DisconnectAsync` resolves from any state without simulated delay and returns the fully initial status.
- [x] A `DisconnectAsync` issued while a connection attempt is pending wins: after the pending `ConnectAsync` completes, the status is fully initial (no resurrected connection).
- [x] `SendCommandAsync` while connected: the immediately following status read shows `RequestedCommand` = issued command and `ConfirmedCommand` unchanged; after completion `ConfirmedCommand` = issued command, with at least the 250 ms acknowledgement delay elapsed.
- [x] Two overlapping commands, after both complete, leave `RequestedCommand` = `ConfirmedCommand` = the last issued command; source inspection confirms confirmations are applied in request order.
- [x] `SendCommandAsync` while Offline throws `DroneNotConnectedException` and leaves the status unchanged; the same applies while Connecting.
- [x] `DroneNotConnectedException` is a `public sealed` type in `DroneControl.Application` deriving from `Exception`.
- [x] `SetSpeedAsync` while connected updates the speed; while Offline or Connecting it resolves and leaves the speed unchanged.
- [x] Direct `SetSpeedAsync(150)` on the controller while connected throws `ArgumentOutOfRangeException` and leaves the speed unchanged (never clamped).
- [x] Every status returned by the controller leaves `Api` at its default value (`Offline`).
- [x] Cancelling during the connection delay throws `OperationCanceledException` and leaves the status fully initial.
- [x] State lives on the controller: two `DroneService` instances constructed over a single `MockDroneController` observe the same simulated connection state.
- [x] `Program.cs` registers `IDroneController` → `MockDroneController` as singleton and `IDroneService` → `DroneService` as scoped, in the same change; nothing else in `Program.cs` changes.
- [x] The API starts in the Development environment (eager validation constructs the complete graph) and `GET /api/health` returns HTTP 200 with `{"status":"ok"}`.
- [x] Source inspection: the mock performs no network or hardware I/O; no GPIO pin numbers or hardware constants appear; delays are named constants (700/250) passed to `Task.Delay` with a `CancellationToken`.
- [x] No new endpoints: `/api/health` remains the only route; no test projects are added (Task 17); the frontend is untouched.
- [x] `dotnet build backend/DroneControl.sln` succeeds with 0 warnings and 0 errors.
