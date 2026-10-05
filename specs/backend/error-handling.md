# Backend Error Handling

## Purpose

Task 16 completes the error policy the backend API deferred in Task 12: execution failures currently surface as bare interim 500 responses. This task turns every failure class into a consistent RFC 9457 `application/problem+json` response — 409 for commands issued while disconnected, 503 for an unavailable drone implementation (a reserved contract for the future hardware adapter), 500 with a generic message for unexpected errors — logs unexpected failures, and guarantees production responses carry no stack traces or internal details. The existing 400 validation responses keep working unchanged.

## Dependencies

- `specs/backend/drone-api.md` — owns the five endpoints, the 400 `ValidationProblem` input contract, and the deliberate gap this task fills ("execution failures deliberately unmapped ... interim status is 500"). This spec supersedes that interim note; endpoint shapes and success behavior stay exactly as defined.
- `specs/backend/drone-simulator.md` (Task 11) — `MockDroneController` throws `DroneNotConnectedException` for commands while disconnected and injects no other failures, so the 503/unexpected-500 mappings cannot be produced by the real app and are verified with a throwaway harness.
- `specs/backend/drone-application-service.md` (Task 10) — `DroneNotConnectedException` lives in the Application layer as the detection contract; the new `DroneUnavailableException` joins it there.
- `specs/backend/domain-model.md` — domain `ArgumentOutOfRangeException` for speed is never reachable through the API (the boundary pre-validates), so it needs no wire mapping.
- dotnet skill: global exception-handling middleware, consistent ProblemDetails, custom exception types, "don't expose internal details".

## Scope

- Application layer: new `DroneUnavailableException` type (reserved availability contract for hardware-phase implementations).
- API layer: one exception-mapping component plus registration of the standard ASP.NET Core problem pipeline (`AddProblemDetails` + exception-handler registration + `UseExceptionHandler`) in `Program.cs`.
- Validation: real-app verification of the 409 mapping and production-response cleanliness; a throwaway harness for the 503 and generic-500 mappings; full regression of Task 12's existing behavior.

## Out of Scope

- Any change to endpoints, request/response DTOs, success-path behavior, timings, or simulator state machine.
- Changing the existing 400 `ValidationProblem` shape (missing/unknown `command`, missing/out-of-range `speed`, malformed JSON already respond 400; they must stay byte-compatible).
- ProblemDetails bodies for unmatched routes (404/405) or status-code pages in general.
- Documenting error responses in the OpenAPI metadata.
- Failure injection, retries, circuit breakers, or changing the simulator to ever throw `DroneUnavailableException` (Task 11 decision stands).
- Cancellation mapping for client disconnects (framework concern, response moot once the client is gone).
- Frontend consumption of the new statuses (Tasks 18/19) and permanent automated tests (Task 17).
- Authentication/authorization error surfaces (none exist).

## Architecture

```text
Controller action throws (never catches — controllers stay thin)
        │
        ▼
ExceptionHandlerMiddleware (app.UseExceptionHandler)
        │
        ├─► registered IExceptionHandler: maps known Application exceptions
        │       DroneNotConnectedException  → 409 ProblemDetails
        │       DroneUnavailableException   → 503 ProblemDetails
        │
        └─► unmatched exceptions → default 500 ProblemDetails (generic text),
            exception logged by the diagnostics pipeline
```

Decisions embedded in the design:

1. **Mapping lives in the API layer** (`DroneControl.Api`, e.g. an `Errors/` component) using the standard .NET 8+ recipe (`AddProblemDetails()` + `AddExceptionHandler<T>()` + `app.UseExceptionHandler()`). Application owns the exception types; Domain/Infrastructure/Mock are untouched.
2. **Uniform JSON in every environment**: because the handler sits between the app and the exception, error responses are ProblemDetails JSON in Development too (the HTML developer page no longer renders for exceptions). Developer-facing stack traces move to logs, which the diagnostics pipeline already writes. This keeps the acceptance checks identical across environments and is a stricter "no stack traces" posture than the backlog's production-only requirement.
3. **Status semantics**: 409 Conflict = request is well-formed but conflicts with current drone state (Task 12's input-vs-execution split preserved); 503 = dependency (drone implementation) cannot serve; 500 = unexpected. Validation stays 400.
4. **Detail policy**: for the two known exceptions, `detail` is the authored exception message (safe, human-readable constants owned by Application); for 500s, `detail` is a fixed generic sentence and the real exception never crosses the wire — only into logs.

## Domain Model

Application-layer exception contracts (`DroneControl.Application`):

| Exception | Meaning | Produced today by | Maps to |
| --- | --- | --- | --- |
| `DroneNotConnectedException` (exists) | A command cannot execute while the drone is disconnected | `MockDroneController.ExecuteCommandAsync` | 409 |
| `DroneUnavailableException` (new) | The drone implementation cannot service the request at all | Nothing yet — reserved contract for the future Raspberry Pi adapter | 503 |

`DroneUnavailableException` intentionally has no producer in Phase 2: introducing the type + wire contract now gives the hardware phase a detection channel without inventing hardware failure semantics (AGENTS: no assumed GPIO behavior; the backlog explicitly requires handling "unavailable drone implementation"). The mapping is still acceptance-verified via harness.

## Behavior

Error response contract (all bodies `Content-Type: application/problem+json`, ProblemDetails shape with `type`, `title`, `status`; `detail`/`errors` per case):

| Category | Trigger | Status | Body |
| --- | --- | --- | --- |
| Invalid command | missing `command`, unknown enum value, malformed JSON | 400 | Existing `ValidationProblem` (with `errors`) — unchanged from Task 12 |
| Invalid speed | missing `speed`, out of 0–100 | 400 | Existing `ValidationProblem` — unchanged |
| Command while disconnected | `DroneNotConnectedException` propagates from service | 409 | ProblemDetails; non-empty human `title`/`detail`; no stack |
| Implementation unavailable | `DroneUnavailableException` propagates from service | 503 | ProblemDetails; non-empty `title`/`detail`; logged at warning by the handler |
| Unexpected server error | any other exception | 500 | ProblemDetails with generic fixed `detail`; the exception's type, message, and stack appear in logs only |

Unchanged flows (regression surface): `GET /api/health` 200; `GET /api/drone/status` 200; connect ≥ ~700 ms then 200 status; command-while-connected ≥ ~250 ms ack then 200; speed while disconnected 200 with speed unchanged (offline no-op parity, Tasks 10/11/12); disconnect 200; OpenAPI document 200 in Development. No success-path response changes.

## Error Cases

- Command immediately after `disconnect` → 409 (the primary user-visible change; interim 500 replaced).
- A drone implementation that cannot serve → 503 (harness-verified only; the simulator cannot produce it by design).
- A bug/infrastructure fault mid-request → 500 generic body + logged exception; never a stack trace or internal message in the response, in any environment.
- Client disconnects during a request: ASP.NET Core cancellation behavior, not mapped by this task.
- Unmatched routes/verbs: 404/405 remain bare (no ProblemDetails added — out of scope).

## Security / Safety

- 500 responses expose no exception types, messages, stack traces, or configuration; internal detail goes to logs only.
- Error bodies contain no secrets (all messages are authored constants).
- A 503 or 409 never claims a physical action occurred; success-path semantics (simulated acknowledgements only) are untouched.
- Uniform JSON error responses also avoid leaking environment differences (no dev-mode HTML in responses).

## Testing Scenarios

Executed in order (throwaway artifacts deleted afterward; no test project created — that is Task 17):

1. `dotnet restore` + `dotnet build` the solution → 0 warnings / 0 errors.
2. Run the real API (Development, port 5080) and re-verify the Task 12 regression battery: health 200; status 200; connect ≥ 700 ms; command-while-connected ≥ 250 ms ack; speed-while-disconnected 200 with speed unchanged; 400 battery (missing/unknown command, missing/out-of-range speed, malformed JSON) with unchanged `ValidationProblem` body incl. `errors`; OpenAPI 200.
3. New 409: `connect` → `disconnect` → `POST /api/drone/command` → 409, `application/problem+json`, ProblemDetails fields present, no stack-trace text.
4. Production cleanliness: restart the API with `ASPNETCORE_ENVIRONMENT=Production`; repeat the 409 check and a 400 check → same ProblemDetails JSON, no stack text, no dev HTML; health still 200.
5. Throwaway harness (temporary minimal web host referencing the real API mapping component + Application types, running on a scratch port): an action throwing `DroneUnavailableException` → 503 ProblemDetails; an action throwing `InvalidOperationException` → 500 ProblemDetails with generic detail, response body contains no exception type/message/stack, and the host log contains the exception; harness deleted after verification.
6. Regression: `dotnet build` still clean; `npm test` still 99/99; file inventory limited to the two source additions + `Program.cs`.

## Acceptance Criteria

- [x] The only source changes are: `DroneUnavailableException` (Application), one API-layer exception-mapping component, and `Program.cs` registrations; no endpoint, DTO, Domain, Infrastructure, simulator, frontend, or configuration file is modified; no NuGet dependency is added.
- [x] `dotnet restore` and `dotnet build backend/DroneControl.sln` succeed with 0 warnings and 0 errors.
- [x] After `connect` then `disconnect`, `POST /api/drone/command` returns HTTP 409 (replacing the interim 500) with `Content-Type: application/problem+json` and a ProblemDetails body with non-empty `title` and `detail`.
- [x] The 409 response body contains no stack trace, exception type name, or inner exception text, in both Development and Production runs.
- [x] A harness action throwing `DroneUnavailableException` yields HTTP 503 ProblemDetails (non-empty `title`/`detail`, no stack), proving the reserved contract's mapping even though the simulator cannot produce it.
- [x] A harness action throwing an unexpected exception yields HTTP 500 with a ProblemDetails body whose `detail` is the generic fixed message; the response contains no exception type, message, or stack trace anywhere.
- [x] The unexpected-failure case is logged: the host's log output contains the exception (type/message or stack) while the response does not.
- [x] All Task 12 success-path behavior is unchanged: health 200; `GET status` 200 JSON; `connect` ≥ ~700 ms then 200; connected command ≥ ~250 ms ack then 200; speed while disconnected 200 with speed unchanged (offline no-op); OpenAPI document 200 in Development.
- [x] All Task 12 validation 400s are unchanged: missing/unknown `command`, missing/out-of-range `speed`, and malformed JSON still return 400 `ValidationProblem` bodies including the `errors` member.
- [x] Running with `ASPNETCORE_ENVIRONMENT=Production`, the API still starts via `dotnet run`, serves health 200, and produces the same ProblemDetails JSON for the 409 and 400 cases (no HTML developer page in any environment).
- [x] The throwaway harness and any temporary files are deleted after validation; the API process started for validation is stopped.
- [x] `npm test` (frontend) still reports 99/99 passing (untouched).
