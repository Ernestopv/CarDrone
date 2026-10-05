# Backend Tests

## Purpose

Task 17 creates the automated test suite for the backend: three xUnit projects under `backend/tests/` covering domain validation, command handling, speed validation, connection rules, simulator behavior, and API endpoints. These tests permanently capture contracts that were previously verified only by throwaway harnesses and runtime checklists — including the Task 16 error mappings (`DroneUnavailableException` → 503 and unexpected → generic 500), which have no producer in the running app and now get first-class coverage via test doubles. No hardware tests exist yet or are added here.

## Dependencies

- `specs/backend/domain-model.md` — the invariants Domain.Tests assert: initial `DroneState`/`DroneStatus` values and `Speed` 0–100 rejection (`ArgumentOutOfRangeException`, never clamps, validated in both initializer and `with` updates).
- `specs/backend/drone-application-service.md` (Task 10) — `DroneService` behavior to pin: `Api=Connected` stamping, delegation with cancellation tokens, application-boundary speed validation, exception pass-through.
- `specs/backend/drone-simulator.md` (Task 11) — every simulator behavior becomes a test: connecting transition observable mid-flight, guarded repeat connects, immediate disconnect reset, epoch supersession of in-flight connects/confirmations, command-while-disconnected throw, requested-vs-confirmed semantics with named constants `ConnectionDelayMs=700`/`CommandAckDelayMs=250`, semaphore-ordered concurrent confirmations, cancellation returning to the initial state, offline speed no-op (including the documented out-of-range-while-offline no-op), and out-of-range-while-connected throw.
- `specs/backend/drone-api.md` (Task 12) — wire contract: camelCase properties, lowercase enum values, numeric enums rejected, 400 `ValidationProblem` battery.
- `specs/backend/error-handling.md` (Task 16) — status mappings to lock in: 409/503/500 ProblemDetails shapes, no-leak guarantees, and the note that 503/500 were previously harness-only; these tests replace the throwaway approach permanently.
- dotnet skill: xUnit as the test framework; `WebApplicationFactory<Program>` for in-process API tests.
- `tasks/BACKLOG.md` Task 17 — suggested project layout, six test categories, "No hardware tests yet."

## Scope

- Three test projects at the backlog-suggested paths, added to `backend/DroneControl.sln`:
  - `backend/tests/DroneControl.Domain.Tests/` (references Domain)
  - `backend/tests/DroneControl.Application.Tests/` (references Application + Infrastructure — the simulator implements the Application contract and the backlog structure has no Infrastructure.Tests; this is the placement decision)
  - `backend/tests/DroneControl.Api.Tests/` (references Api; uses `WebApplicationFactory`)
- Hand-written test doubles only: a recording/throwing `IDroneController` fake (no mocking library).
- The single production seam Api.Tests requires: `public partial class Program { }` appended to `Program.cs` (zero behavior change).
- Determinism policy for timing-dependent behavior (lower-bound-only assertions).
- Validation via `dotnet test` (two consecutive runs), build cleanliness, and frontend regression.

## Out of Scope

- Any production behavior change: no new endpoints, no logic fixes, no visibility changes beyond the Program marker line; all existing sources stay byte-identical.
- A mocking framework (Moq/NSubstitute), FluentAssertions, coverage tooling/thresholds, CI integration, test runners beyond `dotnet test`.
- Hardware/GPIO/Raspberry Pi tests of any kind (backlog: none yet), device access, or GPIO keywords in test code.
- Out-of-process/e2e tests (no real port binding, no `docker` involvement, no frontend↔backend integration — that is Phase 4).
- Performance/load testing, exact-timing assertions, snapshot/golden-file frameworks.
- Frontend test changes (the 99-test suite stays as-is).
- Docker/Compose file changes (the image publishes only `DroneControl.Api`; test projects are unreferenced by production code, so the build is unaffected).

## Architecture

```text
backend/
├── src/            (unchanged except Program.cs marker line)
└── tests/
    ├── DroneControl.Domain.Tests/        → asserts Task 9 invariants
    ├── DroneControl.Application.Tests/   → DroneService + MockDroneController
    └── DroneControl.Api.Tests/           → WebApplicationFactory HTTP tests
```

Reference graph (tests reference production, never the reverse):

```text
Domain.Tests        → Domain
Application.Tests   → Application, Infrastructure
Api.Tests           → Api (+ Api → Application, Infrastructure transitively)
```

Test seams and doubles:

- **`FakeDroneController : IDroneController`** (Application.Tests + Api.Tests each own one): records every call and token, supports scripted outcomes (`ThrowOnSend` = not-connected / unavailable / generic exception, delay-free success). It is instant by design, so API tests are fast and deterministic; timing behavior is tested against the real `MockDroneController` at the Application layer instead.
- **Api.Tests hosts**: the default factory keeps real DI (real `MockDroneController` singleton) for wire-contract and 400/200 tests; mapping tests use `WithWebHostBuilder` to replace `IDroneController` with the fake (last-registered-wins) to drive 409/503/500 and no-leak assertions — the permanent replacement for Task 16's throwaway harness.
- **Isolation**: each `WebApplicationFactory` instance builds its own host with its own singleton simulator; xUnit's per-class collection parallelism is therefore safe. No test may assert against state shared across classes.
- **Determinism policy**: timing tests assert lower bounds only, with machine-jitter headroom (e.g. `≥ ConnectionDelayMs - 100`); no upper bounds, no `Thread.Sleep`, no exact-duration assertions. Concurrency order is observed through completion-order checkpoints, not sleep racing (see Behavior).

## Behavior

Category → coverage map (backlog's six test categories):

| Category | Where | Key assertions |
| --- | --- | --- |
| Domain validation | Domain.Tests | Initial `DroneState` = offline/offline/stop/stop/0 and `DroneStatus` defaults; `Speed` accepts 0 and 100; −1 and 101 throw `ArgumentOutOfRangeException` at construction and via `with`; value is never clamped |
| Command handling | Application.Tests, Api.Tests | Service passes commands to controller untouched and propagates its exceptions; simulator: offline/connecting command throws `DroneNotConnectedException`, connected command records `RequestedCommand` immediately and `ConfirmedCommand` only after the ack delay; fake-throws variants observed as 409/503/500 over HTTP |
| Speed validation | Domain.Tests, Application.Tests, Api.Tests | Domain range rule (above); service rejects out-of-range **before** calling the controller (fake records no call); simulator offline no-op (including out-of-range-while-offline no-op) and throw-and-state-unchanged while connected; API 400 battery: missing `speed`, 120, −5 |
| Connection rules | Application.Tests, Api.Tests | Connect → connecting observable immediately, connected/streaming/rpi-connected after lower-bound delay; repeat connect while connecting/connected is a guarded no-op (connection never resets); disconnect is immediate full reset; disconnect supersedes in-flight connect (attempt completes, state stays offline); cancellation faults with `OperationCanceledException` and returns state to initial; connect→disconnect→connect lifecycle over HTTP returns 200 statuses |
| Simulator behavior | Application.Tests | Requested-vs-confirmed semantics; two concurrent commands confirm in request order via completion checkpoints (first task completes ⇒ `ConfirmedCommand` == first; final state ⇒ second); disconnect during in-flight confirmation skips the stale confirmation; confirmation delay respects `CommandAckDelayMs` lower bound |
| API endpoints | Api.Tests | Health 200; `GET status` wire: camelCase, lowercase enums, `api` connected, `raspberryPi` offline; `POST command` valid → 200 status JSON; missing/unknown `command` and malformed JSON → 400 with `errors`; unknown route → 404 (not 500); 409/503/500 ProblemDetails shapes + no-leak (marker string absent) using the fake |

Test-for-behavior rule: every assertion above must come from the referenced specs, never from re-reading the implementation's incidental details — tests pin contracts, not code shape.

## Validation

```text
dotnet restore backend/DroneControl.sln           → success
dotnet build backend/DroneControl.sln             → 0 warnings, 0 errors
dotnet test backend/DroneControl.sln              → all pass, 0 failed
dotnet test (second consecutive run)              → identical pass (determinism gate)
npm test (frontend)                               → 99/99, frontend/ untouched
```

## Platform Requirements

- .NET 10 SDK with the xUnit templates/packages restorable offline-cache or network access at first restore.
- Api.Tests run on the in-memory TestServer (`WebApplicationFactory`) — no firewall prompts, no real ports.
- `dotnet test` against the solution must work from the repository root and inside `backend/`.

## Testing Scenarios

1. Create the three projects (`net10.0`, xUnit), add them to `DroneControl.sln`, append the Program marker line.
2. Domain.Tests: invariant suite above (~6–10 facts/theories).
3. Application.Tests: `DroneService` delegation via recording fake; `MockDroneController` lifecycle suite (one fresh simulator per test — no shared instances), including timing lower bounds, supersession, cancellation, confirmation ordering, and speed paths.
4. Api.Tests: two fixture styles (real DI; fake-injected DI) covering wire contract, 400 battery, 404, connect/disconnect flow, and the 409/503/500 error mappings with no-leak marker checks.
5. Run the Validation section commands in order; investigate and fix any flakiness surfaced by the second `dotnet test` run (never by loosening assertions without cause).
6. Inventory check: only `Program.cs` (marker line), the new `tests/` tree, and `.sln` changed.

## Acceptance Criteria

- [x] `backend/tests/` contains exactly `DroneControl.Domain.Tests`, `DroneControl.Application.Tests`, and `DroneControl.Api.Tests`; all three are xUnit projects (packages limited to `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, plus `Microsoft.AspNetCore.Mvc.Testing` for Api.Tests) and are registered in `backend/DroneControl.sln`.
- [x] The only production-source change is the appended `public partial class Program { }` marker in `Program.cs`; every other file under `backend/src/` (including all `.csproj` files) is byte-identical (hash-verified) and no test project is referenced by any production project.
- [x] `dotnet build backend/DroneControl.sln` reports 0 warnings and 0 errors, and `dotnet test backend/DroneControl.sln` finishes with 0 failed tests.
- [x] `dotnet test` passes on two consecutive runs (determinism gate); timing tests assert only lower bounds derived from the named constants `ConnectionDelayMs`/`CommandAckDelayMs` with jitter headroom, and no test uses `Thread.Sleep`.
- [x] Domain tests prove: initial `DroneState`/`DroneStatus` values, `Speed` boundary acceptance (0, 100), and `ArgumentOutOfRangeException` rejection at −1 and 101 in both construction and `with` updates, with no clamping.
- [x] Application tests prove the `DroneService` contract: `Api=Connected` stamping with all other fields preserved, delegation of connect/disconnect/command with the controller's exceptions propagating, and out-of-range speed rejected before the controller is called (recording fake observes zero calls).
- [x] Application tests prove the simulator lifecycle: immediate connecting transition observable mid-flight; connected/streaming after the connection lower bound; guarded repeat connect; immediate disconnect reset; disconnect superseding an in-flight connect; cancellation faulting with `OperationCanceledException` and restoring the initial state.
- [x] Application tests prove command semantics: `DroneNotConnectedException` while offline and while connecting; `RequestedCommand` recorded before ack; `ConfirmedCommand` updated only after the ack lower bound; two concurrent commands confirmed in request order via completion checkpoints; disconnect during an in-flight ack leaves no stale confirmation.
- [x] Application tests prove speed paths on the simulator: offline no-op (including the documented out-of-range-while-offline no-op), in-range set while connected, and `ArgumentOutOfRangeException` with unchanged speed while connected and out of range.
- [x] Api tests with real DI prove the wire contract: health 200; `GET /api/drone/status` JSON is camelCase with lowercase enum values and `"api":"connected"`; valid command 200; missing/unknown command and malformed JSON 400 with `errors`; missing/out-of-range speed 400; unknown route 404 (never 500).
- [x] Api tests with the fake controller prove the Task 16 mappings end-to-end: `DroneNotConnectedException` → 409, `DroneUnavailableException` → 503, unexpected exception (message containing a unique marker) → 500 — all with `application/problem+json`, wire status matching body `status`, and no exception type, marker, or stack text in any 500 body.
- [x] No test or project file mentions GPIO, pin numbers, devices, or hardware abstractions; no test binds a real network port (in-memory TestServer only); `MockDroneController` and all hardware-boundary rules remain untouched.
- [x] The frontend is untouched: `npm test` still reports 99/99 and the inventory shows zero changes under `frontend/`.
- [x] All six backlog categories (domain validation, command handling, speed validation, connection rules, simulator behavior, API endpoints) are covered by at least one passing test, demonstrable from the test names/projects.
