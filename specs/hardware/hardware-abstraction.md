# Hardware Abstraction

## Purpose

Task 22 creates the application-level hardware abstraction reserved by the unified runtime design (Task 21, decision D3): an `IDroneHardware` contract in the Application layer plus its software reference implementation `MockDroneHardware` in Infrastructure. This is the seam through which future Raspberry GPIO/PWM providers (Task 23), fail-safe behavior (Task 26) and dry-run suppression (Task 27) will be introduced **without any change to Domain, Application, or API code**.

Task 22 is explicitly software-only: no GPIO behavior, no Raspberry dependencies, no DI wiring changes. The existing simulated pipeline (`MockDroneController` behind `IDroneController`) keeps serving the running application byte-identically — the abstraction is introduced and proven by tests first, wired later.

Verification level delivered by this task: `implemented` / `built` / `mock-tested`. Nothing Raspberry-Pi-runtime or hardware-related is claimed (prerequisites remain owned by Tasks 23–30).

## Dependencies

- `specs/architecture/runtime-deployment.md` (Task 21, binding) — decision D3 fixes this seam's placement (`IDroneHardware` is consumed **below** `IDroneController`, selected at the composition root *later*); D5 fixes the fail-fast philosophy this contract must honor (unavailable hardware is a loud error, never a silent no-op); the prerequisites ledger forbids inventing pin/PWM semantics here.
- `specs/backend/drone-application-service.md` + `specs/backend/drone-simulator.md` — `IDroneController`/`MockDroneController` semantics (connection state machine, 700/250 ms simulated pacing, ack honesty) are **frozen** by this task and must not be refactored "to use" the new seam; `DroneUnavailableException` (Task 16) is the pre-existing channel for hardware-unavailability, reused rather than duplicated.
- `specs/backend/domain-model.md` — semantic `DroneCommand` values and the speed 0–100 reject-don't-clamp invariant are reused, not reinvented.
- `specs/backend/error-handling.md` — the exception→status mapping the Application layer already owns; new exception types are forbidden here.
- `specs/backend/backend-tests.md` (Task 17) — xUnit conventions, project layout, placement of Infrastructure-implementation tests inside `DroneControl.Application.Tests`, determinism rules.
- dotnet skill + existing code style — file-scoped namespaces, `Async` suffix, default `CancellationToken` parameters, XML docs, no external packages.
- `AGENTS.md` — hardware isolation rules (GPIO knowledge never above Infrastructure; React never sees pins), "do not introduce abstractions not needed yet" (this one is backlog-required), never claim hardware success without real confirmation.
- The `raspberry-pi` specialized skill referenced by the SDD instructions is **not installed** in this workspace; the equivalent rules (no invented wiring/PWM/device semantics, verification-level honesty) are enforced through this spec's own constraints and Task 21's prerequisites ledger.

## Scope

- Application layer: `IDroneHardware` interface + its result/availability value types (contract only — the vocabulary for "receive semantic commands, apply speed, expose availability, report execution result" from the backlog responsibilities).
- Infrastructure layer: `MockDroneHardware` — deterministic, immediate, platform-free software implementation of the contract.
- Unit tests (in `DroneControl.Application.Tests`, referencing Infrastructure like the existing simulator tests) proving the contract behaviors.
- One-line status updates in `docs/ARCHITECTURE.md` marking the `IDroneHardware` seam as implemented while Pi/device elements remain `design`.

## Out of Scope

- `RaspberryDroneHardware` and any Raspberry/platform code → Task 23 (`specs/hardware/raspberry-hardware-provider.md`) — its backlog goal is literally "Create the Raspberry Pi-specific hardware provider behind `IDroneHardware`".
- Any GPIO, PWM, wiring, pin-semantics, device-path, or frequency/duty content → Tasks 24–30; Task 22's own requirement says "Do not implement real GPIO behavior yet".
- DI registration or composition-root changes: no service is registered, no mode is read, `Program.cs` stays untouched. There is no consumer until Task 23 wires selection (Task 23 requirement: "Support configuration-based runtime selection"). Wiring an unused service now would be dead code, not an abstraction.
- `HARDWARE_MODE`/`CAMERA_MODE` configuration plumbing (design fixed in Task 21; validation/selection owned by Tasks 22+ consumers — i.e., Task 23's composition root).
- Fail-safe (Task 26), dry-run wrapper/decorator implementation (Task 27), motor direction mapping (Task 29), PWM speed control (Task 30).
- Any change to `IDroneController`, `MockDroneController`, `DroneService`, Domain, API, frontend, Docker/compose, or existing tests.
- Refactoring `MockDroneController` to consume `MockDroneHardware` — explicitly forbidden below ("Coexistence" in Architecture).

## Architecture

```text
                    (unchanged, live today)
API controllers → IDroneService → DroneService → IDroneController → MockDroneController
                                                              
        NEW seam (Task 22) — not yet wired anywhere:
                    IDroneHardware  (Application)
                        △
                        │ implemented by
                    MockDroneHardware (Infrastructure)      ← proven by unit tests now
                        △
                        └ (reserved shape) RaspberryDroneHardware → Task 23
                              ↓ later consumed BY a hardware-backed IDroneController implementation (Task 23)
                              ↓ later wrapped/interpreted by dry-run (Task 27) and fail-safe (Task 26)
```

Contract placement rules (from D3 + AGENTS layering):

- `IDroneHardware` and its value types live in **`DroneControl.Application`**; they may reference only `DroneControl.Domain` types and the BCL. No GPIO/PWM/pin/device vocabulary may exist anywhere in the Application layer.
- `MockDroneHardware` lives in **`DroneControl.Infrastructure`** (next to `MockDroneController`), with zero external packages and zero OS/hardware calls.
- **Decorator-friendly by construction**: one interface method per operation, no static/singleton state, cancellation on every operation — so Tasks 26/27 can wrap the seam without touching upper layers (constraint, not speculative code).
- **Coexistence, not duplication**: `MockDroneController` (application-contract simulator with timing/ack semantics) and `MockDroneHardware` (contract-level software hardware without timing) are different layers of the end-state architecture; Task 22 must not merge or rewire them. How mock mode eventually composes both (e.g., whether Task 23's controller path also serves mock) is decided by Task 23/35 specs — this task only guarantees both can exist.
- The contract deliberately contains **no connect/disconnect and no acknowledgement state machine** — those are `IDroneController` concerns. `IDroneHardware` answers exactly: "are you present?; apply this command; apply this speed; what happened?".

## Domain Model

New value types (Application layer, alongside the existing exception types):

- `HardwareAvailability` — enum: `Available`, `Unavailable`. Presence of the hardware layer only; rich health detail is deferred to the Pi provider's tasks.
- `CommandExecutionResult` — record/class with `Status` (enum `HardwareCommandStatus { Applied, Rejected }`) and optional `Reason` string. **Semantics are software-level**: `Applied` means the hardware layer accepted and performed its (mock) operation — it must never be documented or treated as physical actuation confirmation (AGENTS honesty + Task 21 ledger: the wire carries no hardware-source field).

Reused unchanged: `DroneCommand` (five semantic values, `Stop` included), speed range rule (0–100 reject-never-clamp), `DroneUnavailableException`.

## Behavior

- `GetAvailabilityAsync()` → `Available` from `MockDroneHardware`, always (software implementation; the mock models presence, not failure).
- `ExecuteCommandAsync(command)` → on an available implementation: returns `Applied` (with `Reason` null) and records the command as the last applied value; on an **unavailable** implementation: throws `DroneUnavailableException` — never `Rejected`, never silent no-op (D5 fail-fast: rejection is for logical refusals by a working layer; absent hardware is an error condition).
- `ApplySpeedAsync(speed)` → validates 0–100 **before** recording: boundaries accepted; `-1`/`101` throw `ArgumentOutOfRangeException` and leave state unchanged; never clamps (defense-in-depth mirror of the Application service rule, same exception type as Domain).
- All operations honor `CancellationToken` (cooperative, passed through; cancellation surfaces as `OperationCanceledException` from awaiting callers — no custom cancellation logic).
- `MockDroneHardware` is fully synchronous-fast internally (completed tasks), deterministic, safe to instantiate per test, no shared statics, no timers/delays.
- No behavior whatsoever changes for the running application: the new types are unreferenced by DI, controllers, or the simulator.

## Interfaces

Contract sketch (shape is normative; member naming is an implementation detail within these meanings):

```csharp
namespace DroneControl.Application;

public interface IDroneHardware
{
    Task<HardwareAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<CommandExecutionResult> ExecuteCommandAsync(
        DroneCommand command, CancellationToken cancellationToken = default);

    Task ApplySpeedAsync(int speedPercent, CancellationToken cancellationToken = default);
}
```

- XML docs on every member, including the Applied≠physical-confirmation honesty note and the unavailable-throws-`DroneUnavailableException` rule.
- No new members beyond the three responsibilities; connect/disconnect and status snapshots stay out (owned by `IDroneController`).

## Validation

- Speed range is validated at this layer too (0–100 inclusive; reject, never clamp) — consistent boundary for every future provider implementation.
- Commands are constrained by the existing `DroneCommand` enum; no string parsing at this seam.
- Vocabulary purity (mechanically checkable): `Gpio`, `Pin`, `PWM`, `L298N`, `/dev/` must not appear in `DroneControl.Application` sources or in the new Infrastructure types (pin *numbers* obviously cannot appear — they're not referenced at all in Task 22).

## Error Cases

- Hardware layer unavailable during `ExecuteCommandAsync`/`ApplySpeedAsync` → `DroneUnavailableException` (reuses the existing Application→503 channel; the API never invents a second "hardware gone" contract).
- Speed out of range → `ArgumentOutOfRangeException`, state unchanged.
- Cancellation mid-operation → standard `OperationCanceledException`; mock completes synchronously so this path is only exercised through the parameter plumbing contract (documented, not simulated with fake delays).
- No physical-error taxonomy exists yet (motor stall, power loss, wiring faults...) — that vocabulary is explicitly **forbidden** in this task; the `Rejected` status exists precisely so future logical refusals don't need contract changes.

## Platform Requirements

- Builds and tests on the existing Windows/Linux dev toolchain (`net10.0`, dotnet SDK 10) with **no Raspberry Pi, no hardware, no new NuGet packages**.
- The contract must remain satisfiable by a `linux/arm64` implementation (Task 23's platform requirement) — by containing nothing platform-specific, it does; no ARM64 build/validation is claimed or needed for Task 22 itself (existing multi-arch image build stays green as regression).

## Security / Safety

- Physical safety is structural: Task 22 contains **no actuation code at all** — no GPIO/PWM access exists to misuse.
- Honesty rules carried into the contract's documentation: `Applied` ≠ hardware confirmation; `ConfirmedCommand` semantics and simulated-ack labeling stay exactly as specified in Tasks 10–12/16 (frozen).
- Stop semantics remain a normal semantic command (`DroneCommand.Stop`) at this seam too — no special fail-safe logic before Task 26.
- React/frontend surface: zero exposure to pins/ GPIO (nothing is added to the wire at all).

## Testing Scenarios

Executed in order:

1. Source inspection: new Application files (`IDroneHardware`, result/availability types) + `MockDroneHardware` in Infrastructure; `Program.cs`/`csproj`/existing sources unchanged (hash); vocabulary grep (Validation section) clean; no DI registration added (Program.cs hash proves it).
2. New unit tests (xUnit, in `DroneControl.Application.Tests`): availability default; command → `Applied` + recorded state; command when unavailable (test-local fake implementing `IDroneHardware` with `Unavailable`) → `DroneUnavailableException` per the documented contract; speed boundaries 0/100 accepted; speed −1/101 → `ArgumentOutOfRangeException` + unchanged state; cancellation token pass-through (mock honors signature-level; optional pre-cancelled-token behavior via test fake); result-type honesty check (no "confirmed"-flavored member names on `CommandExecutionResult` — compile-visible).
3. `dotnet build` → 0 warnings / 0 errors; `dotnet test` → existing 51 + new suite, 0 failures, on **two consecutive runs** (existing determinism convention).
4. API behavior unchanged: existing Api.Tests (15/15, real-DI WAF host included) prove no startup/DI drift; no live HTTP validation required since no wiring changed (state it plainly rather than inventing runtime claims).
5. Frontend untouched: no `frontend/` file changes (inventory); `npm test` 121/121 by convention.
6. `docs/ARCHITECTURE.md`: flip the `IDroneHardware` seam bullet from `design — Tasks 22–24` to reflect the seam now implemented (mock) with Raspberry provider still design; MEMORY update for durable conventions (contract placement, unavailable-throws rule, Applied≠confirmed honesty).

## Acceptance Criteria

- [x] New source files are limited to the Application layer (interface + `HardwareAvailability` + `CommandExecutionResult`/`HardwareCommandStatus`) and Infrastructure (`MockDroneHardware.cs`), plus new test files under `DroneControl.Application.Tests`; `Program.cs`, all existing `.cs`/`.csproj`/`.sln`/frontend/Docker files are byte-identical (hash-verified); no NuGet package added anywhere.
- [x] `IDroneHardware` exposes exactly the three backlog responsibilities — availability query, semantic-command execution returning an execution result, speed application — with cancellation on every operation, and contains no connect/disconnect/ack-state members.
- [x] No GPIO/PWM/pin/L298N/device-path vocabulary appears in any Application-layer file or in `MockDroneHardware` (grep-verified); `DroneControl.Application` still has zero dependencies beyond `DroneControl.Domain`.
- [x] `CommandExecutionResult` offers only software-honest outcomes (`Applied`/`Rejected` + optional `Reason`); its XML docs state that `Applied` is not physical confirmation; no member/type implies hardware acknowledgement.
- [x] The documented unavailable contract holds and is tested: operations on an unavailable hardware implementation throw the existing `DroneUnavailableException` (no silent no-op, no new exception type); availability is reported via `HardwareAvailability` with the mock always `Available`.
- [x] `MockDroneHardware` validates speed before recording (0/100 accepted; −1/101 throw `ArgumentOutOfRangeException`; state unchanged on reject; never clamps), records the last applied command, is deterministic/instant/static-free, and runs on Windows without any Pi-specific API (compilation + tests are the proof level).
- [x] The live pipeline is untouched: `IDroneController`, `MockDroneController`, `DroneService`, API controllers, Domain records are byte-identical (hash); no DI registration or configuration key is introduced; existing 51 backend tests pass unchanged alongside the new ones.
- [x] `dotnet test` reports 0 failures including ≥ 8 new hardware-contract tests (availability, command applied/recorded, unavailable→exception, speed boundaries ×2, speed reject ×2 unchanged-state, plus one contract-fake or cancellation case) and passes on two consecutive runs.
- [x] `dotnet build` reports 0 warnings / 0 errors; frontend is untouched (inventory) with `npm test` 121/121 as regression.
- [x] Nothing Raspberry-specific was built or claimed: no Task 23 provider type exists; ARM64/Pi runtime/GPIO/PWM verification criteria are explicitly marked not-applicable for this task (software-only scope), matching the verification-level vocabulary from Task 21.
- [x] `docs/ARCHITECTURE.md` seam status and MEMORY are updated to record the implemented abstraction, the unavailable-throws rule, and the Applied≠confirmed honesty constraint for downstream hardware tasks.
