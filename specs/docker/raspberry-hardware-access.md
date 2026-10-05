# Raspberry Pi Docker Hardware Access

## Purpose

Define the Docker runtime boundary that allows Raspberry Pi hardware access
to be configured without changing application source code.

This task establishes the configuration mechanism, Compose boundary,
least-privilege strategy, and documentation required for later Raspberry Pi
runtime validation.

Target-specific device paths, group IDs, ownership, and permissions do not
need to be known to complete this task.

Those values are discovered and verified later on the target Raspberry Pi.

## Dependencies

- `specs/architecture/runtime-deployment.md` — D1/D2/D3/D5, runtime modes,
  profile/configuration boundaries, and the prerequisites ledger.
- `specs/hardware/raspberry-hardware-provider.md` — the Infrastructure provider
  and its `IDroneHardware` boundary from Task 23.
- `specs/hardware/hardware-abstraction.md` — the application-facing contract;
  Docker configuration must not leak into it.
- `specs/docker/backend-container.md` and `specs/docker/multi-arch.md` — the
  existing non-root, architecture-neutral backend image and ARM64 build
  workflow.
- `docker-compose.yml` — the existing `backend` service and `HARDWARE_MODE`
  passthrough established by Task 23.
- Task 25 GPIO abstraction, Task 26 fail-safe, Task 27 dry-run, and Task 30
  PWM specifications — downstream consumers of the verified interfaces.

## Scope

- Define a configuration-driven mechanism for Raspberry Pi hardware access.
- Keep PC mock mode free of Raspberry Pi device requirements.
- Define where Raspberry Pi device mappings and permissions will be supplied.
- Ensure no Raspberry Pi device path or group ID is hardcoded without evidence.
- Document the target-Pi inventory procedure for later runtime validation.
- Preserve the backend non-root runtime by default.
- Preserve the existing backend image and source tree.
- Verify the backend image builds for `linux/arm64`.
- Verify PC mode continues to run through Docker Compose.
- Record Raspberry Pi-specific device and permission values as deferred runtime configuration.
- Preserve the existing `USER app` non-root runtime unless a documented,
  tested permission strategy proves a different arrangement is necessary.
- Keep the default PC `mock` mode device-free and functional.
- Verify the backend image still builds for `linux/arm64` and that the final
  container configuration remains compatible with the existing backend image.
- Document the verification level of each result separately: built, mock
  tested, dry-run tested, Pi-runtime verified, or real-hardware verified.

## Out of Scope

- Implementing or changing `IDroneHardware`, `IDroneController`, API
  controllers, domain types, or application behavior.
- Implementing GPIO line operations, pin direction, HIGH/LOW states, motor
  direction, L298N wiring, PWM frequency, duty-cycle limits, or electrical
  safety rules.
- Selecting semantic meanings for GPIO23, GPIO24, GPIO21, GPIO20, GPIO12, or
  GPIO13. The known values remain configuration only until their owning
  specifications establish their meaning.
- Implementing fail-safe, watchdog, shutdown, timeout, or recovery behavior.
- Implementing dry-run behavior or camera/uStreamer device access.
- Adding a separate Raspberry Pi service, a second backend implementation, or
  a remote hardware agent.
- Making the container privileged by default. `privileged: true` is permitted
  only as a separately justified, documented finding if least-privilege
  access is proven impossible.
- Claiming that a successful ARM64 image build proves Raspberry Pi runtime or
  physical hardware operation.

## Architecture

```text
same backend image/source
        |
        +-- PC + HARDWARE_MODE=mock
        |      no Raspberry devices, no hardware permissions
        |
        +-- Pi + HARDWARE_MODE=dry-run|real
               Compose configuration
                 -> verified host device mappings
                 -> verified non-root group permissions
                 -> Infrastructure provider
                 -> IDroneHardware
```

Hardware-specific knowledge stops at the Docker/Infrastructure boundary.
Domain, Application, API, and React remain unaware of device paths, Linux
groups, Docker privileges, and GPIO/PWM implementation details.

The existing single backend service and image lineage are reused. The Compose
design must make hardware exposure conditional on the Pi configuration while
leaving the default PC service free of Raspberry device mappings. If Compose
cannot conditionally omit a device mapping on the same service, the chosen
profile/configuration arrangement must be documented and validated rather than
silently introducing a second control service or a fake PC device.

## Deferred Raspberry Pi Validation

Target-specific Raspberry Pi values are intentionally deferred.

The following may remain `NOT VERIFIED` during this task:

- actual `/dev/...` GPIO device paths;
- actual GPIO/PWM interface names;
- host ownership;
- host group IDs/names;
- non-root hardware permissions;
- Raspberry Pi runtime access;
- physical GPIO access.

These deferred checks do not block completion of Task 24.

They will be validated during the later Raspberry Pi runtime and end-to-end
deployment tasks.

Use `BLOCKED` only if the configuration mechanism itself cannot be implemented
without target-specific information.

## Domain Model

No new Domain or Application types are required.

The deployment configuration has these conceptual values:

- `HARDWARE_MODE`: existing `mock | dry-run | real`; Task 23 owns parsing and
  startup selection.
- `GPIO/PWM device configuration`: verified host-to-container mappings and
  permission identifiers, supplied only by deployment configuration.
- Pi hardware activation/profile: a Compose configuration value defined by the
  implementation after the conditional-device mechanism is verified.

Device paths, group IDs/names, and interface names are facts to be discovered
on the target Pi. They are not specified here as constants.

## Behavior

### PC mode

With `HARDWARE_MODE=mock` and no Pi hardware configuration:

- `docker compose config` succeeds without requiring Raspberry devices.
- `docker compose up` starts the existing frontend/backend stack.
- The backend uses the existing mock graph.
- No host device is opened, mounted, or required.

### Pi hardware mode

With an explicitly prepared Pi configuration and `HARDWARE_MODE=real` or
`dry-run`:

- Compose exposes only the verified device interfaces required by the provider.
- The container receives only the verified permission groups required to open
  those interfaces.
- The backend remains non-root unless the implementation records and approves
  a narrowly scoped alternative.
- Missing devices, invalid mappings, or insufficient permissions fail clearly;
  they never fall back to `mock`.
- Hardware operations remain subject to the provider and later GPIO/PWM/fail-safe
  specifications. Docker access alone does not imply that an operation was
  applied or physically confirmed.

### Configuration changes

Switching between PC and Pi must require only deployment configuration and/or
Compose profile selection. No source-code, Dockerfile, Domain, Application,
API, or frontend change is required.

## Interfaces

The implementation must define and document:

1. A host inventory procedure that records the target Pi OS/kernel, available
   GPIO/PWM interfaces, device nodes, ownership, and group permissions.
2. The Compose configuration contract for verified device mappings and group
   identifiers.
3. The activation mechanism that prevents PC mode from requiring Pi devices.
4. A startup/preflight outcome for missing devices or permissions, including a
   clear log message and no silent mode substitution.

The existing application interfaces remain unchanged:

```text
ASP.NET Core -> IDroneHardware -> Raspberry provider -> Linux interfaces
```

No device path or permission value is part of an HTTP request, response, domain
model, or frontend configuration.

## Validation

- Every device mapping is backed by target-Pi evidence and names the interface
  it serves; guessed `/dev` paths are rejected.
- Every permission is tested using the actual container runtime user and is
  recorded as the minimum required access. Host root access alone is not proof
  that the non-root container can use the interface.
- `privileged: true` is absent unless a least-privilege investigation and
  target-Pi test demonstrate it is unavoidable; the reason and residual risk
  must be documented.
- `HARDWARE_MODE` remains strictly validated by the Task 23 composition root.
  Docker configuration must not reinterpret invalid values or fall back to
  `mock`.
- PC configuration renders no Raspberry device requirement and starts with
  mock hardware.
- Pi configuration renders only the verified mappings and permission changes.
- ARM64 image build succeeds using the Task 15 workflow.

## Error Cases

- A required host device is absent: hardware-enabled startup fails or reports
  unavailable according to the existing provider contract; it does not select
  mock mode.
- The container user lacks required permission: startup/health validation
  reports the missing permission clearly; no physical operation is claimed.
- A configured path or group is invalid: Compose/preflight validation fails
  before hardware operation.
- A PC is given Pi hardware mode: the existing Task 23 platform preflight
  remains authoritative and fails fast.
- Docker cannot apply the requested mapping: deployment fails visibly and the
  operator is directed to the recorded host prerequisite; no privileged
  fallback is attempted.
- The target Pi exposes an interface different from the recorded inventory:
  the deployment is treated as unverified and must not claim Pi-runtime or
  real-hardware success until revalidated.

## Platform Requirements

- Backend runtime: the existing .NET 10 image, built for `linux/arm64`.
- Target host: Raspberry Pi OS/runtime details must be discovered and recorded;
  this specification does not assume a board model, kernel, GPIO API, PWM API,
  or device numbering scheme.
- Docker Engine/Compose must support the selected device and group mapping
  mechanism.
- Development PC: Windows/Linux Docker Desktop or Linux Docker Engine with no
  Raspberry hardware dependency in default mode.
- The existing non-root `app` user is the default container identity.

## Security / Safety

- Least privilege is mandatory: expose the minimum verified devices and groups,
  never the entire host device tree.
- Do not use host networking, host filesystem mounts, or blanket privileges as
  substitutes for device-specific access.
- Hardware mode intent is fail-fast: `real` and `dry-run` must never silently
  become `mock`.
- Docker access is not physical verification. Acceptance must distinguish
  container built, Pi runtime verified, dry-run tested, and real hardware
  verified.
- No motor can be considered stopped, energized, or directionally correct from
  this task alone; those claims belong to Tasks 26–30 and later hardware tests.

## Testing Scenarios

1. Inventory the target Pi and preserve the evidence used to select every
   device mapping and permission.
2. Render the default PC Compose configuration; confirm no Raspberry device
   mapping or Pi-only permission is required.
3. Start the PC stack with `HARDWARE_MODE=mock`; verify health and existing API
   behavior, with no hardware claim.
4. Render the Pi hardware configuration on the target runtime; verify only the
   inventory-backed mappings and groups are present.
5. Start the hardware-enabled backend with the actual non-root container user;
   verify provider preflight/access behavior without claiming actuation.
6. Remove or invalidate one required device/permission; verify a clear failure
   and no silent fallback to mock.
7. Run the existing `linux/arm64` image build and inspect the resulting image.
8. Run existing backend/frontend regression tests; no application contract or
   wire-shape changes are expected.
9. Clean up temporary Compose environments and ensure no validation artifacts,
   host device mappings, or privileged containers remain enabled by default.

## Acceptance Criteria

- [x] The Docker/Compose configuration provides an explicit boundary for
      Raspberry Pi hardware device and permission configuration.

- [x] The default PC configuration requires no Raspberry Pi devices,
      group IDs, or hardware permissions.

- [x] `HARDWARE_MODE=mock` works through the existing Docker Compose stack.

- [x] Switching to Raspberry Pi hardware mode does not require application
      source-code or Dockerfile changes.

- [x] No unverified Raspberry Pi device path, group ID, or permission is
      hardcoded into the project.

- [x] The backend continues to run as the existing non-root user by default.

- [x] `privileged: true` is not enabled by default.

- [x] The backend image builds successfully for `linux/arm64`.

- [x] A documented Raspberry Pi inventory procedure exists for discovering
      actual devices, ownership, groups, and permissions later.

- [x] Existing frontend/backend regression tests pass.

Deferred validation:

- [ ] NOT VERIFIED — Actual Raspberry Pi GPIO/PWM device paths.
- [ ] NOT VERIFIED — Actual Raspberry Pi device ownership/groups.
- [ ] NOT VERIFIED — Non-root access to Raspberry Pi GPIO/PWM interfaces.
- [ ] NOT VERIFIED — Raspberry Pi container runtime hardware access.

Deferred Raspberry Pi validation does not block completion of this task.
