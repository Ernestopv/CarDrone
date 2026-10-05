# Drone REST API

## Purpose

Task 12 exposes the backend drone functionality over HTTP: five endpoints under `/api/drone` that delegate to `IDroneService`, validate request input at the API boundary, and serialize status through the wire mapping the domain model deferred to this layer. Controllers stay thin — no business logic, no state, no exception handling (error policy belongs to Task 16).

This is also the first task where the simulated drone becomes observable over HTTP: the singleton simulator's state persists across separate requests, proving the cross-request behavior Task 11 established but could not demonstrate without endpoints.

## Dependencies

- `specs/backend/backend-setup.md` — Api project responsibilities (HTTP endpoints, dependency injection, OpenAPI, CORS); existing `GET /api/health` convention.
- `specs/backend/domain-model.md` — the canonical wire mapping (Interfaces → Wire Mapping Boundary): PascalCase C# enums serialize as lowercase strings (`forward`, `connected`, `streaming`, …), property names use the ASP.NET Core camelCase default (`raspberryPi`), no serialization attributes in Domain, no GPIO or pin information in any contract, `DroneStatus` is the status reporting shape with nested `DroneState`.
- `specs/backend/drone-application-service.md` — `IDroneService` operations consumed by the controller; it already declares "REST endpoints, controllers, request/response DTOs — Task 12".
- `specs/backend/drone-simulator.md` — the behavior visible over HTTP: 700 ms connect, 250 ms acknowledgement, no-op rules (connect while connected, speed while offline), `DroneNotConnectedException` raised for commands while disconnected.
- Frontend contract reference (adaptation, not duplication): `frontend/src/services/droneService.ts` shows what Tasks 18/19 will build on top of this API; mapping the domain response shape to the UI shape (including synthesizing `CommandAck`) is the frontend service abstraction's job, not this task's.

## Scope

- `DroneController` in `DroneControl.Api` with exactly five actions:
  - `GET  /api/drone/status`
  - `POST /api/drone/connect`
  - `POST /api/drone/disconnect`
  - `POST /api/drone/command` (JSON body `{ "command": "forward" }`)
  - `PUT  /api/drone/speed` (JSON body `{ "speed": 42 }`)
- Request DTOs for the command and speed bodies, living in the Api project.
- Input validation at the API boundary returning HTTP 400 for invalid requests.
- JSON wire configuration for enum values (lowercase) in the API layer.
- `Program.cs` wiring: controller services registered, controllers mapped, existing health/CORS/OpenAPI intact.
- Validation by runtime HTTP verification against the running API, source inspection, and build.

## Out of Scope

- Error policy for execution failures: mapping `DroneNotConnectedException` (command while disconnected), unavailable implementations, and unexpected server errors to status codes; the consistent error envelope; logging; hiding stack traces — Task 16. This task contains no `try`/`catch` and no exception-to-status conversion.
- Durable test projects — Task 17.
- The frontend HTTP client and any response-shape adaptation to the UI types — Tasks 18/19.
- Docker, Compose, multi-arch builds — Tasks 13–15.
- Authentication, rate limiting, versioning, SignalR/real-time push — no requirement exists yet.
- New endpoints beyond the five listed; changes to `/api/health`; hardware behavior of any kind.

## Architecture

```text
HTTP request
    ↓
DroneController (Api)        input validation only; no business logic, no try/catch
    ↓ IDroneService (Application, injected)
DroneService → IDroneController → MockDroneController (simulator)
    ↓
DroneStatus (Domain) serialized by the API layer:
camelCase property names + lowercase enum values (domain-model wire table)
```

- **Controller style:** an MVC `[ApiController]` named `DroneController` under `[Route("api/drone")]`, per the backlog requirement to keep controllers thin. The existing `/api/health` minimal-API route coexists unchanged.
- **Delegation only:** each action validates input (or relies on automatic model binding), awaits the `IDroneService` call, and returns the resulting `DroneStatus`. For mutating actions the returned status comes from a follow-up `GetStateAsync`, so responses reflect post-operation state with `Api` stamped `Connected`.
- **Wire mapping ownership:** the API layer configures enum serialization (`JsonStringEnumConverter` with a camelCase naming policy) — Domain stays attribute-free, exactly as the domain model's wire table requires. Property names use the framework's camelCase default.
- **Response shape:** the domain `DroneStatus` as-is: `{ "state": { "connection", "camera", "requestedCommand", "confirmedCommand", "speed" }, "raspberryPi", "api" }`. It deliberately has no `failedCommand` (frontend-only state since Task 9) and no `services` grouping — the frontend service abstraction maps to its UI shape in Tasks 18/19.
- **Program.cs gains** controller registration (`AddControllers`), controller mapping (`MapControllers`), and the JSON enum converter configuration. Nothing existing is removed or altered.

## Behavior

| Route | Request | Success | Notes |
| --- | --- | --- | --- |
| `GET /api/drone/status` | none | 200 + current `DroneStatus` | `Api` = `connected` |
| `POST /api/drone/connect` | no body required | 200 + status after the attempt | returns after ≥ 700 ms when the drone was offline; already connected → immediate 200 with unchanged status (simulator no-op) |
| `POST /api/drone/disconnect` | no body required | 200 + fully initial status | immediate; safe from any state |
| `POST /api/drone/command` | `{ "command": "<value>" }` | 200 + status with `requestedCommand` and `confirmedCommand` equal to the issued command | returns after ≥ 250 ms acknowledgement while connected |
| `PUT /api/drone/speed` | `{ "speed": <0-100> }` | 200 + status with the new speed | while disconnected the simulator ignores the change; the response then reports the unchanged speed (parity with the frontend mock) |

Additional rules:

- All five endpoints await the service call to completion before responding — a 200 means the operation completed (for this phase: the simulator completed it, never physical hardware).
- Responses serialize enums only as the canonical lowercase values from the domain-model wire table (`forward`, `backward`, `left`, `right`, `stop`, `offline`, `connecting`, `connected`, `streaming`, `error`).
- Requests deserialize command values from the same lowercase spellings.
- Endpoints pass the request's abort token (`HttpContext.RequestAborted`) to the service so a disconnected client cancels in-flight waits.
- Connection state persists across HTTP requests (the simulator is a singleton): a `GET /status` after `POST /connect` reports `connected`.

## Interfaces

### Request DTOs (Api project)

```jsonc
// POST /api/drone/command
{ "command": "forward" }   // required; one of forward|backward|left|right|stop

// PUT /api/drone/speed
{ "speed": 42 }            // required; integer 0-100 inclusive
```

### Response body (all five endpoints)

The domain `DroneStatus`, camelCased:

```jsonc
{
  "state": {
    "connection": "connected",
    "camera": "streaming",
    "requestedCommand": "stop",
    "confirmedCommand": "stop",
    "speed": 0
  },
  "raspberryPi": "connected",
  "api": "connected"
}
```

## Validation

Input rules enforced at the API boundary (before the service is called):

1. `command` is required: a body missing it, or containing an unknown value, returns 400. It must never be interpreted as a default command (`Stop` or `Forward`).
2. `speed` is required: a body missing it, an explicit `null`, a non-integer value, or a value outside 0–100 returns 400. It must never silently default to 0.
3. Malformed JSON returns 400.
4. Bodies on `GET /status`, `POST /connect`, `POST /disconnect` are not required and are ignored if present.
5. Invalid input never reaches `IDroneService` — the speed range is rejected at the boundary, so the service's own pre-validation and the domain invariant remain independent deeper layers, not the primary API guard.

Validation failures use ASP.NET Core's automatic `[ApiController]` responses (HTTP 400). Standardizing the error body is Task 16's job.

## Error Cases

| Case | Task 12 behavior |
| --- | --- |
| Missing/unknown `command` | 400 (input validation) |
| Missing/`null`/non-integer/out-of-range `speed` | 400 (input validation) |
| Malformed JSON body | 400 (model binding) |
| Wrong content type on body endpoints | 415 (framework default) |
| Unknown route | 404 (framework default) |
| Command while disconnected (`DroneNotConnectedException`) | not mapped by this task — an unhandled exception surfaces as 500 until Task 16 defines it (expected 409-class handling there) |
| Unexpected server errors, unavailable drone implementation | not mapped by this task — Task 16 (envelope, logging, no stack traces) |

The interim 500 for execution failures is a documented gap, not a claimed-correct status code: Task 16 owns every row below input validation.

## Platform Requirements

- .NET 10 / ASP.NET Core MVC controllers registered and mapped alongside the existing minimal-API health route.
- No new NuGet packages; no `.csproj` changes; solution remains 4 projects; no test projects.
- OpenAPI continues to work: `/openapi/v1.json` in Development lists the five drone routes plus `/api/health`.
- Development CORS policy remains as configured; no CORS changes are required for these same-origin endpoints.
- The API must start in Development with the existing `ValidateOnBuild` graph intact.

## Security / Safety

- No GPIO, pin, or hardware information appears in any route, request DTO, or response — contracts expose only Domain types.
- A 200 response is returned only after the awaited operation completed at the controller level; the API never acknowledges a command before the downstream acknowledgement (simulated in this phase — responses must not be interpreted as hardware confirmation).
- Invalid input is rejected before it reaches the drone service, so a malformed body can never default into a real command such as `forward`.
- No authentication exists yet; the API is a local-development surface (no requirement defines auth — recorded as an accepted current-state limitation, not an oversight).

## Testing Scenarios

Runtime verification against the running API (Development, `--urls http://localhost:5080`), executed in order so each request builds on the previous state:

1. `GET /api/drone/status` → 200; initial values, all enums lowercase, `api: "connected"`.
2. `POST /api/drone/connect` → 200 after ≥ 700 ms; `connection: "connected"`, `camera: "streaming"`, `raspberryPi: "connected"`.
3. `GET /api/drone/status` → 200 `connected` — state persists across separate HTTP requests.
4. `POST /api/drone/command {"command":"forward"}` → 200 after ≥ 250 ms; requested/confirmed `forward`.
5. `PUT /api/drone/speed {"speed":42}` → 200; `state.speed: 42`.
6. `PUT /api/drone/speed {"speed":150}` → 400; follow-up `GET` still shows 42.
7. `PUT /api/drone/speed {}` → 400; follow-up `GET` still shows 42 (no silent zero).
8. `POST /api/drone/command {"command":"hover"}` → 400.
9. `POST /api/drone/command {}` → 400 (never interpreted as a default command).
10. Malformed JSON body → 400.
11. `POST /api/drone/disconnect` → 200; body fully initial.
12. `GET /api/drone/nope` → 404; `GET /api/health` still → 200; `/openapi/v1.json` lists exactly the five drone routes.

Source inspection:

13. `DroneController` injects only `IDroneService`; contains no `try`/`catch`; references no `IDroneController`, `MockDroneController`, Infrastructure types, or GPIO identifiers.
14. Request DTOs live in the Api project; Domain/Application gain no serialization attributes or HTTP types.
15. `Program.cs` additions only: controller registration, controller mapping, JSON enum configuration; health/CORS/OpenAPI untouched.

Build and regression:

16. `dotnet build backend/DroneControl.sln` — 0 warnings, 0 errors.
17. Frontend `npm test` regression (unchanged frontend).

## Acceptance Criteria

- [x] `GET /api/drone/status` returns HTTP 200 with `state.connection: "offline"`, `state.camera: "offline"`, `state.requestedCommand: "stop"`, `state.confirmedCommand: "stop"`, `state.speed: 0`, `raspberryPi: "offline"`, `api: "connected"` — all enum values lowercase.
- [x] `POST /api/drone/connect` (no body required) returns HTTP 200 after at least 700 ms with `state.connection: "connected"`, `state.camera: "streaming"`, `raspberryPi: "connected"`.
- [x] Connection state persists across requests: a subsequent `GET /api/drone/status` reports `"connected"`.
- [x] `POST /api/drone/command` with `{"command":"forward"}` returns HTTP 200 after at least 250 ms with `state.requestedCommand` and `state.confirmedCommand` both `"forward"`.
- [x] `PUT /api/drone/speed` with `{"speed":42}` returns HTTP 200 with `state.speed: 42`.
- [x] `PUT /api/drone/speed` with `{"speed":150}` returns HTTP 400 and a follow-up `GET` still reports `state.speed: 42` (invalid input never reaches the service).
- [x] `PUT /api/drone/speed` with `{}` (missing `speed`) returns HTTP 400; speed is never silently defaulted to 0.
- [x] `POST /api/drone/command` with `{"command":"hover"}` returns HTTP 400.
- [x] `POST /api/drone/command` with `{}` (missing `command`) returns HTTP 400; the body is never interpreted as a default command.
- [x] A malformed JSON body on `POST /api/drone/command` returns HTTP 400.
- [x] `POST /api/drone/disconnect` (no body required) returns HTTP 200 with the fully initial status (offline, offline, stop, stop, 0, `raspberryPi: "offline"`).
- [x] Every enum value observed in any response is one of the canonical lowercase wire values defined in `domain-model.md` (no PascalCase enum serialization anywhere).
- [x] `GET /api/drone/nope` returns HTTP 404; `GET /api/health` still returns HTTP 200 with `{"status":"ok"}`.
- [x] `/openapi/v1.json` (Development) lists exactly the five drone routes plus `/api/health` as application routes.
- [x] `DroneController` injects only `IDroneService` and contains no `try`/`catch` and no exception-to-status conversion (execution-failure mapping is exclusively Task 16's).
- [x] `DroneController` and request DTOs reference no `IDroneController`, `MockDroneController`, Infrastructure types, GPIO, pin, or hardware identifiers.
- [x] Endpoints pass the request abort token to service calls (source inspection).
- [x] `Program.cs` changes are limited to controller registration, controller mapping, and JSON enum configuration; the existing health route, CORS, and OpenAPI setup remain intact, and the API still starts in Development.
- [x] No new NuGet packages, no `.csproj` changes, solution unchanged (4 projects), no test projects added, frontend untouched.
- [x] `dotnet build backend/DroneControl.sln` succeeds with 0 warnings and 0 errors.
