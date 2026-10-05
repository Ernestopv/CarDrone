# Raspberry Pi Hardware Provider

## Purpose

Task 23 creates the Raspberry Pi hardware provider behind the Task 22 seam and makes runtime mode selection real: the composition root now reads `HARDWARE_MODE` and wires either today's simulator (`mock`, default) or the Raspberry provider path (`real`) — **with no source-code change required between modes** (the Task 21 design's headline property).

Because physical prerequisites are still open (device paths → Task 24, GPIO abstraction → Task 25, wiring semantics → Task 28, direction/PWM → Tasks 29/30), the provider is deliberately **inert**: it exists, is platform-gated, reports itself `Unavailable`, and every operation fails loudly via the existing `DroneUnavailableException` → 503 channel. That is the truthful state of a Pi backend that cannot yet touch hardware — and per the backlog's own "Do not implement unverified motor mappings", any command that *appeared* to actuate would be a lie. Tasks 24–30 replace the inert internals layer by layer without touching anything above the seam.

## Dependencies

- `specs/hardware/hardware-abstraction.md` (Task 22, binding) — the `IDroneHardware` contract, `CommandExecutionResult` honesty semantics, the "unavailable → throw, never no-op" rule, and the pessimistic enum defaults this provider must honor. Task 22 explicitly deferred DI wiring to this task ("selection is Task 23's config-based runtime selection").
- `specs/architecture/runtime-deployment.md` (Task 21, binding) — D3 (selection at composition root only; provider consumed below `IDroneController`), D5 (fail-fast, no silent fallback; `dry-run` semantics fixed but *enabled* by Task 27), the prerequisites ledger (nothing physical may be invented here), the verification ladder, and the config keys `HARDWARE_MODE` (`CAMERA_MODE` remains untouched).
- `specs/backend/drone-application-service.md` — the `IDroneController` contract this task's bridge must satisfy without change; `DroneUnavailableException` is the pre-existing unavailability channel.
- `specs/backend/drone-api.md` + `specs/backend/error-handling.md` — the wire contract and error mapping are frozen; the hardware mode must be invisible to clients (same JSON shapes, 503 already defined).
- `specs/docker/docker-compose-development.md` / `full-stack-compose.md` — the compose `environment:` mechanism this task extends with one passthrough line (D2: `.env` drives platform differences).
- `specs/docker/multi-arch.md` (Task 15) — the proven `linux/arm64` build workflow reused to verify the new Infrastructure code **builds** for arm64 (build level only; Pi *runtime* remains unverified).
- AGENTS.md — controllers/API never touch GPIO; hardware code isolated in Infrastructure; never claim hardware success without real confirmation.
- dotnet skill (DI, configuration, async conventions); docker skill (compose env, arm64 builds). The `raspberry-pi` skill referenced by the SDD instructions is not installed in this workspace; its rules (no invented device paths/permissions, minimal exposure, verification honesty) are enforced as explicit constraints below.

## Scope

- Infrastructure: `RaspberryDroneHardware : IDroneHardware` (inert, honest, pure BCL) and `HardwareDroneController : IDroneController` (the bridge that consumes `IDroneHardware` — the D3 "hardware-backed controller" that makes `real` mode a complete, bootable graph).
- API composition root: a `HARDWARE_MODE` selection helper (parsed once at startup) registering `mock` (default → today's exact graph), `real` (provider + bridge behind an OS/arch preflight that aborts startup on unsupported platforms), rejecting `dry-run` (recognized, reserved to Task 27) and unknown values with explicit errors.
- One `docker-compose.yml` environment passthrough: `HARDWARE_MODE: ${HARDWARE_MODE:-mock}` — default-preserving, `.env`-driven per D2.
- Unit tests (fakes both ways: available and unavailable hardware) + startup/selection tests against the helper + an arm64 **build** verification.
- Status/doc updates (`docs/ARCHITECTURE.md`, MEMORY).

## Out of Scope

- Any GPIO/PWM access, device paths, permissions, `/dev/` anything, or pin-number usage → Tasks 24/25; the provider must not even read the `GPIO` config section (nothing consumes it yet).
- Motor mappings of any kind (command→lines, direction, enable pins) → Tasks 28/29; "do not implement unverified motor mappings" is this task's governing constraint.
- `dry-run` behavior (log-and-suppress wrapping) → Task 27; here `HARDWARE_MODE=dry-run` is only *recognized-and-rejected* at startup.
- Fail-safe, watchdogs, signal-loss behavior → Task 26.
- Camera/`CAMERA_MODE`, uStreamer, video → Tasks 31–33.
- Changes to Domain, Application contracts (`IDroneController`, `IDroneService`, `IDroneHardware` — the interfaces themselves are frozen), API controllers/DTOs, the HTTP wire, frontend (anything), existing providers (`MockDroneController`), Dockerfiles, `.dockerignore`, or any NuGet package.
- Raspberry Pi hardware runtime validation of any kind (no Pi present; `real` mode's Pi-side behavior stays `NOT VERIFIED`).
- Connect/disconnect semantics beyond the bridge rules defined here (radio/link concepts do not exist in this phase: availability is the truth).

## Architecture

```text
                      composition root (Api, selection helper — the ONLY platform-aware spot)
HARDWARE_MODE ──parse/validate──┬── mock (default) ──► IDroneController → MockDroneController      (today's graph, untouched)
                                └── real  ──preflight(Linux+Arm64, else abort startup)──►
                                        IDroneHardware   → RaspberryDroneHardware   (inert: Unavailable, ops throw)
                                        IDroneController → HardwareDroneController  (bridge, consumes IDroneHardware)
                                                           ↓ D3: provider stays below the controller contract
API/Application/Domain: unchanged, never see hardware concepts     (GPIO/PWM internals: Tasks 24/25 fill the provider)
```

- Both new classes live in **Infrastructure**; the selection helper + mode enum live in the **Api** project (composition root code belongs to the root — D3). Only the helper touches `RuntimeInformation`/configuration.
- No packages: the provider is pure BCL, keeping ARM64 compatibility trivial (build-verified) and deferring every hardware-library/device decision to Task 24 where it can be made *on evidence*.
- Singleton lifetimes mirror `MockDroneController` (stateful session object consumed by scoped `DroneService`).
- The wire stays byte-identical: mode is an implementation choice behind interfaces, invisible to clients — proven by untouched existing Api.Tests.

## Domain Model

New types (all Api/Infrastructure, none in Domain/Application interfaces):

- `HardwareMode` (Api): `Mock`, `DryRun`, `Real` — parsed from the exact strings `mock`/`dry-run`/`real` (case-insensitive, trimmed); parse/validation errors are startup aborts, never defaults.
- Provider availability source (Task 23): constant `HardwareAvailability.Unavailable` with reason **"hardware actuation is not implemented yet (Tasks 24/25)"** surfaced in the exception message; the config-driven/real-probe source replaces it later without contract change.
- `IDroneController` session semantics for the bridge (see Behavior) — no new domain states; `ConnectionStatus` enum values are reused as-is.

## Behavior

### Selection rules (composition root)

| `HARDWARE_MODE` | Result |
| --- | --- |
| unset / `mock` | exactly today's registrations; no other behavior touched |
| `real` + platform unsupported (not Linux **and** ARM64) | **abort startup**: `HostAbortedException`, message names the mode, the observed OS/arch, and that Pi hardware access requires a Raspberry Pi runtime |
| `real` + platform supported | register `RaspberryDroneHardware` + `HardwareDroneController`; startup logs one warning that actuation is not yet implemented (honest posture) |
| `dry-run` | abort startup: "recognized; dry-run mode is provided by Task 27" — never silently treated as mock or real |
| anything else | abort startup: invalid value listing the accepted strings |

No branch may catch-and-continue: every rejection path terminates before `app.Run()`.

### `RaspberryDroneHardware` (IDroneHardware)

- `GetAvailabilityAsync` → `Unavailable` (always, in this task; documented).
- `ExecuteCommandAsync` / `ApplySpeedAsync` → throw `DroneUnavailableException("… not implemented …")`; **speed range invariant runs first** (bad speed still throws `ArgumentOutOfRangeException` — invariants are platform-independent, matching Task 22 rules).
- Honors cancellation like every other operation.

### `HardwareDroneController` (IDroneController bridge)

Mirrors the existing simulator's observable contract minus any simulation timing:

- `GetStateAsync`: `connection` = session state (initial `Offline`); `camera` = `Offline` (no video pipeline exists in this mode — honest, never simulated streaming); `RaspberryPi` = `Connected` **iff** the hardware probe reports `Available` (in Task 23: never, so `Offline`); `Api` left unstamped (DroneService rule preserved); `requestedCommand`/`confirmedCommand` reflect only commands this session actually had `Applied` (initially `Stop`/`Stop`); `speed` = last applied speed (initial 0).
- `ConnectAsync`: probe availability → `Available` marks the session connected (immediate; no fabricated delay); `Unavailable` → `DroneUnavailableException` (→ existing 503). 
- `DisconnectAsync`: returns the session to `Offline`, rest commands, speed 0 (same visible effect as the simulator's reset, achieved by forgetting session state, not by simulating hardware).
- `SendCommandAsync`: not connected → `DroneNotConnectedException` (parity with mock's 409 path — the state gate is part of the frozen application contract); connected → `IDroneHardware.ExecuteCommandAsync` (Task 23: always throws `DroneUnavailableException` → 503). On `Applied`: `requestedCommand` = `confirmedCommand` = command (the hardware layer executed its software-level operation; no simulated delay, no claim of physical movement — the wire still carries no ack-source field and the frontend keeps labeling acks `simulated`, per the Task 21 ledger).
- `SetSpeedAsync`: connected → range-check + `ApplySpeedAsync` (on `Applied`, record speed); not connected → silent no-op **with unchanged state** (deliberate wire-parity with the simulator, documented; commands — which actually claim action — stay strict).

### Compose

`docker-compose.yml` backend gains `HARDWARE_MODE: ${HARDWARE_MODE:-mock}` — a plain `docker compose up` behaves identically to today (default), while a Pi folder's `.env` (Tasks 24/34 finalize) can select `real`. On PC, setting `HARDWARE_MODE=real` in `.env` must make the backend container **fail at startup** with the preflight message in its logs (loud, observable, reversible).

## Interfaces

```csharp
// Api project (composition root)
public static class DroneRuntimeSelection
{
    // registers the mode-appropriate implementations into `services`;
    // throws HostAbortedException on rejected/incompatible modes
    public static void AddDroneRuntime(this IServiceCollection services, IConfiguration configuration);
}

// Infrastructure
public sealed class RaspberryDroneHardware : IDroneHardware { /* inert provider (Task 23) */ }
public sealed class HardwareDroneController : IDroneController
{
    public HardwareDroneController(IDroneHardware hardware);
}
```

Config key: `HARDWARE_MODE` (env/appsettings; accepted `mock|dry-run|real`, default `mock`). `CAMERA_MODE` explicitly not read by anything yet.

## Validation

- Mode strings validated exactly once at startup (trim, case-insensitive); unknown → abort listing accepted values; `dry-run` → abort naming Task 27. No runtime re-reading.
- Speed invariants re-checked by the provider path before unavailability throws (defense in depth consistent with every existing layer).
- `real` preflight = OS/arch facts only (`RuntimeInformation`); no device probing (would be invented hardware knowledge → Task 24).

## Error Cases

- `real` on PC (or any non-Linux/arm64): startup abort, clear message (observable in `docker logs` and in unit tests).
- `dry-run` before Task 27: startup abort naming the owner task (never silently degraded).
- Malformed value: startup abort listing valid modes.
- Any drone operation in `real` mode while the provider is inert: `DroneUnavailableException` → existing 503 ProblemDetails (no behavior drift, clients unaffected by this task).
- Commands in real mode before connect: `DroneNotConnectedException` → 409 (contract parity).

## Platform Requirements

- Development PC (Windows/Linux x64): default `mock` path byte-compatible with today; `real` must demonstrably *fail to start* (this is the testable half of the preflight).
- `linux/arm64`: the solution must **build and publish** unchanged (pure BCL code; verified by re-running the Task 15 arm64 build). Pi **runtime** behavior (booting `real` on a Pi, device access) is NOT claimed — no hardware present (prerequisites ledger, Task 21).
- Compose v2 variable interpolation for the passthrough; no new images/services introduced.

## Security / Safety

- **Nothing can actuate from this task**: no GPIO/PWM code exists; the provider is a loud no-op path. "Unverified motor mappings" are not implemented, only explicitly blocked.
- Fail-fast everywhere (D5): unsupported/ambiguous configuration never degrades to another mode silently.
- Honesty chain documented and preserved: 503 on every hardware operation; `Applied` is software-level (Task 22 rule); the frontend's `simulated` ack labeling stays conservative — a later wire-contract extension (ledger item) owns any `hardware` claim.
- No pin numbers or GPIO config values appear in or flow from any task-23 code path (they would be unused anyway); React remains hardware-agnostic by wire shape, unchanged.

## Testing Scenarios

Executed in order; nothing throwaway survives except documentation:

1. Inspection + hash audit: changed source limited to two Infrastructure files, one Api helper file, `Program.cs` (registration swap), `docker-compose.yml` (one env line) + new test files; Domain/Application/other Infrastructure/API controllers/DTOs/frontend/Dockerfiles byte-identical; no package additions.
2. Unit tests (Application.Tests — fakes on both sides): provider reports `Unavailable` and throws `DroneUnavailableException` on command/speed (with the range-check-first order proven); bridge state machine with an *available* fake (connect→connected, command records requested+confirmed immediately, speed applied, disconnect resets, not-connected → `DroneNotConnectedException`, offline-speed no-op) and with the *inert real provider* (connect throws; status fields all honest: camera `Offline`, raspberryPi `Offline`, rest state).
3. Selection tests (Api.Tests project, direct helper call on a `ServiceCollection` + in-memory config): unset/`mock` → resolves `MockDroneController` and **no** `IDroneHardware` registration; `real` on this (unsupported) platform → throws `HostAbortedException` with the platform message; `dry-run` → abort naming Task 27; `bogus` → abort listing values; case-insensitivity (`MOCK`) accepted.
4. Default host still boots unchanged: existing 15 Api.Tests pass without modification (they exercise the real DI graph — default mode ⇒ mock); optionally one WAF resolution assert (`IDroneController` is `MockDroneController`).
5. `dotnet build` 0/0 + `dotnet test` all green twice consecutively (determinism).
6. ARM64 **build** verification: `docker buildx build --platform linux/arm64 -t cardrone-backend:arm64-check --load backend/` succeeds → `docker image inspect` → `arm64` → delete the check tag. No run (honest: build only).
7. Compose default-path check: `docker compose config` shows the `HARDWARE_MODE` env with mock default; `docker compose up -d` → backend `healthy` → `GET /api/health` 200 → `down`. **Fail-fast check:** `up -d` with `HARDWARE_MODE=real` in a temporary `.env` → container never healthy, `docker compose logs backend` contains the preflight abort message → delete `.env`, `down` (leaving no local env files).
8. Frontend regression: `npm test` 121/121 unchanged (inventory shows no frontend diff).
9. Docs: `docs/ARCHITECTURE.md` seam/provider statuses flipped (provider+selection implemented, actuation/device/GPIO still design); MEMORY records mode-selection mechanics + honest-inert provider + verification boundary.

## Acceptance Criteria

- [x] Source changes are limited to: `DroneControl.Infrastructure/RaspberryDroneHardware.cs`, `DroneControl.Infrastructure/HardwareDroneController.cs`, `DroneControl.Api/DroneRuntimeSelection.cs` (or equivalently named Api-root helper), `DroneControl.Api/Program.cs`, one environment line in `docker-compose.yml`, and new test files under existing test projects; Domain, Application contracts, existing Infrastructure sources (incl. `MockDroneController`), API controllers/DTOs, frontend, Dockerfiles, and `.csproj`/`.sln` files are byte-identical; no new NuGet packages.
- [x] With `HARDWARE_MODE` unset (or `mock`), the composition root produces today's exact graph: existing 15 API tests pass unmodified, a host resolution check shows `IDroneController → MockDroneController`, and no `IDroneHardware` service is registered.
- [x] `HARDWARE_MODE=real` on this non-arm64 development machine aborts startup via the selection helper with a `HostAbortedException` whose message names the mode, the observed OS/arch, and the Raspberry Pi requirement — proven in a unit test and by a failed compose startup whose container logs show the message; nothing downgrades to mock on any path (code inspection + tests).
- [x] `HARDWARE_MODE=dry-run` aborts startup with a message identifying it as reserved for Task 27 (test); unknown/empty-garbage values abort listing `mock|dry-run|real`; valid values parse case-insensitively; parsing happens once at startup.
- [x] `RaspberryDroneHardware` honors the Task 22 contract honestly: availability is `Unavailable`, `ExecuteCommandAsync`/`ApplySpeedAsync` throw `DroneUnavailableException`, speed range violations throw `ArgumentOutOfRangeException` first (all tested); the class contains no GPIO/PWM/pin/device-path vocabulary or config reads (grep + inspection) and uses only the BCL.
- [x] `HardwareDroneController` satisfies `IDroneController` with the specified session semantics (tested with fakes): probe-gated connect, `DroneNotConnectedException` while disconnected, command recorded as both requested and confirmed only on `Applied` with no simulated delay, honest status (camera `Offline`; `raspberryPi` derived from actual probe), disconnect reset, offline speed no-op parity; no code path fabricates availability or confirmation.
- [x] The HTTP surface is unchanged for clients: same DTOs, same wire, 503 mapping reused from Task 16 (no new endpoints or fields; existing API test battery green untouched).
- [x] `docker-compose.yml` passes `HARDWARE_MODE` through with default `mock`; `docker compose config` renders it; a default `up -d` keeps the whole stack healthy (identical to today), and the compose fail-fast scenario (step 7) is executed with the `real`-on-PC logs check and full cleanup (temp `.env` removed, containers down).
- [x] The solution builds and the backend image **builds for `linux/arm64`** with the new code (Task 15 workflow re-run, image inspected `arm64`, check tag deleted); no claim is made about running on a Pi — Raspberry Pi runtime and all physical behavior remain **NOT VERIFIED**, consistent with the verification ladder.
- [x] `dotnet build` 0/0, `dotnet test` ≥ 20 new tests (provider, bridge, selection) all passing on two consecutive runs alongside the existing suite; `npm test` 121/121 with zero frontend-file changes.
- [x] `docs/ARCHITECTURE.md` and MEMORY reflect: mode selection implemented (mock-tested), inert provider (honest-unavailable), device/GPIO/motor layers still design; the Task 21 prerequisites ledger remains unamended (this task answers none of its unknowns).
