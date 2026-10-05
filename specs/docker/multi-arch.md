# Multi-Architecture Docker Build

## Purpose

Task 15 proves the Task 13 backend image builds for both `linux/amd64` and `linux/arm64` through the Docker Buildx workflow — the prerequisite for the future Raspberry Pi ARM64 deployment. The deliverables are a validated buildx workflow and documented build commands (`docs/DOCKER.md`). The Dockerfile is expected to need **no changes**: architecture neutrality was designed into Task 13 (no `--platform` pin, framework-dependent publish with no runtime identifier) and this task verifies that neutrality both by inspection and by actually building both architectures.

Build validation only — running the arm64 image (emulated or on real hardware) and publishing to a registry are deliberately out of scope.

## Dependencies

- `specs/docker/backend-container.md` (Task 13) — the Dockerfile being built: multi-stage, official `sdk:10.0`/`aspnet:10.0` images, framework-dependent publish, no `--platform`, no `HEALTHCHECK`. Its neutrality decisions are prerequisites, not things to change.
- `specs/docker/docker-compose-development.md` (Task 14) — the local dev workflow must keep working unchanged; Compose builds the host architecture only.
- `AGENTS.md` — Docker/Raspberry Pi boundary: ARM64 compatibility alone does not imply GPIO access; the image stays portable application code.
- `tasks/BACKLOG.md` Task 15 — four requirements: validate the Buildx workflow, keep the Dockerfile architecture-neutral, introduce no x86-only dependencies, document build commands.

## Scope

- Environment prerequisite: register `linux/arm64` emulation (binfmt/QEMU) so cross-architecture build steps can run, and document it as the cross-arch prerequisite.
- Per-architecture buildx builds loaded into the local image store with distinct validation tags, each verified by inspecting the image architecture.
- One combined single-invocation build targeting `linux/amd64` **and** `linux/arm64` together, with locally verifiable dual-architecture output (no registry exists, so the export is an OCI artifact).
- Verification that the Dockerfile is still architecture-neutral and that no x86-only dependency was introduced (inspection — no edits expected or allowed).
- `docs/DOCKER.md` documenting every validated build command (single-arch, per-arch buildx, combined multi-arch, compose quick reference, binfmt prerequisite).
- Regression of Tasks 13/14 workflows and cleanup of all validation artifacts.

## Out of Scope

- Registry pushes, manifest lists published to a registry, or any registry credentials — the project has no registry.
- **Running** the `linux/arm64` image: no emulated QEMU run and no real Raspberry Pi run in this task. (Building under emulation is in scope; executing the built image is not.)
- Any change to `backend/Dockerfile`, `backend/.dockerignore`, `docker-compose.yml`, application source, or project files — the combined effect of the neutrality and no-new-dependencies requirements.
- Switching Docker Desktop to the containerd image store (a Desktop setting requiring a restart) — the workflow below works with the observed classic image store via OCI export.
- Other architectures (`linux/arm/v7`, `s390x`, …), `docker buildx bake`, CI pipelines, provenance/SBOM/signing strategies.
- A frontend image (none exists) and any hardware/GPIO access — ARM64 readiness does not authorize device access.

## Architecture

```text
docs/DOCKER.md                (new — documented, validated build commands)
backend/Dockerfile            (Task 13 — unchanged, built for both arches)
backend/.dockerignore         (unchanged)
docker-compose.yml            (unchanged — host arch only)
```

```text
[prerequisite] docker run --privileged --rm tonistiigi/binfmt --install arm64
                        │ registers QEMU handlers kernel-wide (throwaway container)
                        ▼
verify: docker buildx ls lists linux/arm64
        docker run --rm --platform linux/arm64 alpine uname -m  → aarch64
                        │
        ┌───────────────┴────────────────────────┐
        ▼                                        ▼
per-arch builds (--load, local store)      one combined invocation
docker buildx build --platform linux/amd64   docker buildx build
  -t cardrone-backend:amd64 --load             --platform linux/amd64,linux/arm64
docker buildx build --platform linux/arm64     --output type=oci,dest=<tarball>
  -t cardrone-backend:arm64 --load           (dual-arch output, no registry needed)
        │                                        │
        ▼                                        ▼
docker image inspect →                       extract + verify BOTH
  Architecture=amd64 / arm64                 linux/amd64 and linux/arm64 present
```

Decisions embedded in the design:

1. **binfmt is a documented prerequisite, not a project dependency.** The environment has no arm64 emulation (verified: an arm64 container fails with `exec format error`), and `docker buildx ls` shows only `linux/amd64 (+3), linux/386`. The standard BuildKit companion image `tonistiigi/binfmt` is run once with `--privileged --rm --install arm64` (kernel-level handler registration, container discarded). It appears in `docs/DOCKER.md` as the cross-arch prerequisite, with a note that it must be re-registered after a Docker VM restart if cross-builds start failing. After registration, `docker buildx ls` must list `linux/arm64` before any arm64 build begins.
2. **Per-arch builds use `--load` with distinct tags.** The image store is the classic Docker store (verified via `docker info` DriverStatus), which stores one architecture per tag but accepts single-platform loads of any architecture. Tags `cardrone-backend:amd64` and `cardrone-backend:arm64` keep Task 13's `cardrone-backend` tag and Task 14's Compose reference untouched. The arm64 build is the real neutrality proof: `dotnet restore` and `dotnet publish` RUN steps execute as arm64 binaries under emulation.
3. **The combined two-platform invocation exports an OCI tarball.** A single `docker buildx build --platform linux/amd64,linux/arm64 … --output type=oci,dest=<tarball>` produces dual-architecture output locally without a registry. If the default builder (docker driver) refuses multi-platform output, the documented workflow creates a dedicated `docker-container` driver builder (`docker buildx create` → build → `docker buildx rm`), which is documented and validated as part of the workflow. Success is judged by the exported artifact containing both architectures, not by the command's exit code alone.
4. **`docs/DOCKER.md` is the single documentation home** for build commands, and every command it lists must be executed successfully during validation — no unvalidated commands get documented (hence no push commands).
5. **Cleanup is part of the task.** Validation tags, the OCI tarball, any temporary builder, and validation containers are removed afterward; the documented commands remain usable for the future.

## Interfaces

`docs/DOCKER.md` must document at least these command classes (each executed during validation):

| Command class | Content |
| --- | --- |
| Cross-arch prerequisite | `tonistiigi/binfmt --install arm64` registration + verification (`docker buildx ls`, arm64 `uname -m` probe) |
| Single-arch build | classic `docker build -f backend/Dockerfile -t cardrone-backend backend/` |
| Per-arch buildx | `docker buildx build --platform linux/amd64 -t cardrone-backend:amd64 …` and `… linux/arm64 -t cardrone-backend:arm64 …` (with `--load`) |
| Combined multi-arch | `docker buildx build --platform linux/amd64,linux/arm64 --output type=oci,dest=… backend/` (including builder setup/removal if the workflow requires it) |
| Compose quick reference | `docker compose up -d`, `docker compose config`, `docker compose down` |

## Behavior

Expected validation lifecycle (execution happens later, not during planning):

1. Prerequisite check: `docker buildx ls` lacks `linux/arm64` → register binfmt → probe succeeds (`aarch64`) → `docker buildx ls` lists `linux/arm64`.
2. `docker buildx build --platform linux/amd64 -t cardrone-backend:amd64 --load backend/` → `docker image inspect` reports `amd64` (fast; native).
3. `docker buildx build --platform linux/arm64 -t cardrone-backend:arm64 --load backend/` → inspect reports `arm64`. This is the slow path: the .NET SDK runs under emulation for restore/publish — expect several minutes, budget generous timeouts, and require a clean success (no fallback to skipping the arm64 RUN steps).
4. Combined invocation exports an OCI tarball containing both platforms; the export is inspected (extracted content shows `amd64` and `arm64` entries) and then deleted.
5. The two images' `Entrypoint`/`ExposedPorts` are compared — identical, proving one image definition across architectures.
6. `docs/DOCKER.md` is written to match the commands actually used; each documented command is then executed as written.
7. Regression: `docker compose config` and a quick compose up/down cycle still work, Task 13's `cardrone-backend` tag is present, `dotnet build` is clean, `npm test` passes, and the file inventory shows `docs/DOCKER.md` as the only created file.
8. Cleanup: validation tags, tarball, temporary builder (if any), and validation containers are removed.

Environmental failures are reported, not spec behavior: a stopped daemon, a failed binfmt registration (e.g., privileged containers blocked), missing `linux/arm64` in `docker buildx ls` after registration (remedy: restart Docker Desktop, re-run registration), emulation crashes during the arm64 SDK build, and insufficient disk space.

## Platform Requirements

- Docker engine with Buildx/BuildKit (observed: BuildKit v0.26.2; builders `default` and `desktop-linux`, both `docker` drivers, listing `linux/amd64 (+3), linux/386` only).
- Host platform `linux/amd64` (Windows Docker Desktop, Linux engine); Windows containers mode unsupported.
- `linux/arm64` binfmt/QEMU registration absent in this environment (observed `exec format error`) — registration via `tonistiigi/binfmt` is a mandatory, documented prerequisite; the classic image store (observed DriverStatus `extfs`) means multi-platform output goes to an OCI artifact, not `--load`.
- Network access: `linux/arm64` layers of both official base images plus a fresh NuGet restore inside the arm64 build (the RUN layer cache is keyed by base-image digest, which differs per architecture).
- Disk headroom for two extra image variants plus the OCI tarball (order of 1 GB).
- Only tooling bundled with Docker (buildx, `tar` on the host for inspecting the export) is used; nothing is added to the project.

## Security / Safety

- The binfmt installer runs `--privileged` — a standard, throwaway container whose sole function is registering emulation handlers in the host kernel; it touches no project code and is discarded with `--rm`.
- No secrets, credentials, or registry endpoints appear in commands or documentation.
- The built images remain Task 13's non-root, portable application code; no devices, no GPIO, no privileged runtime — targeting ARM64 does not grant hardware access (AGENTS.md boundary).
- Validation artifacts (tags, tarball, temporary builder, containers) are removed after use; only the documentation remains.

## Testing Scenarios

Executed in order (throwaway artifacts cleaned up afterward):

1. Prerequisite: `docker buildx ls` (record platforms) → run `tonistiigi/binfmt --install arm64` → `docker buildx ls` lists `linux/arm64` → arm64 `alpine uname -m` → `aarch64`.
2. Source inspection: Dockerfile unchanged from Task 13 — no `FROM --platform`, no `--platform` flag, no RID/`--self-contained` publish, no package-manager installs, no arch conditionals; base images still official multi-arch `sdk:10.0`/`aspnet:10.0`; no `.csproj`/`.sln` changes (no new dependencies of any kind).
3. Per-arch builds: amd64 build + `docker image inspect` → `amd64`; arm64 cross-build + inspect → `arm64`.
4. Combined build: one `--platform linux/amd64,linux/arm64` invocation → OCI export → extract → both `amd64` and `arm64` present in the artifact.
5. Configuration parity: `Entrypoint` and `ExposedPorts` identical between the `:amd64` and `:arm64` images.
6. Documentation audit: `docs/DOCKER.md` exists with all five command classes; every command in it is executed successfully as written.
7. Regression: `docker compose config` OK; a quick `docker compose up -d` → `docker compose down` cycle OK; `cardrone-backend` (Task 13 tag) still present; `dotnet build backend/DroneControl.sln` → 0 warnings/0 errors; `npm test` → 99/99; file inventory shows only `docs/DOCKER.md` created.
8. Cleanup: remove `:amd64`/`:arm64` tags, the OCI tarball, any temporary builder, and validation containers.

## Acceptance Criteria

- [x] `docs/DOCKER.md` exists and documents: the binfmt/QEMU cross-arch prerequisite, the single-arch build command, the per-architecture buildx commands for `linux/amd64` and `linux/arm64`, the combined `--platform linux/amd64,linux/arm64` build command, and a compose quick reference — and every command it lists was executed successfully during validation.
- [x] `docs/DOCKER.md` is the only file created by this task; no application source, `.csproj`, `.sln`, `backend/Dockerfile`, `backend/.dockerignore`, `docker-compose.yml`, or `.gitignore` file is modified.
- [x] The Dockerfile remains architecture-neutral by inspection: no `FROM --platform`/`--platform` flag, no runtime identifier or `--self-contained` publish, no package-manager installation lines, and no architecture conditionals; base images remain `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0`.
- [x] After registering the prerequisite, `docker buildx ls` lists `linux/arm64` before any arm64 build starts, and an arm64 probe container reports `aarch64` (`docker run --rm --platform linux/arm64 alpine uname -m`).
- [x] `docker buildx build --platform linux/amd64 -t cardrone-backend:amd64 --load backend/` succeeds, and `docker image inspect cardrone-backend:amd64 --format '{{.Architecture}}'` reports `amd64`.
- [x] `docker buildx build --platform linux/arm64 -t cardrone-backend:arm64 --load backend/` succeeds as a genuine cross-build (its restore/publish steps execute under emulation), and `docker image inspect cardrone-backend:arm64 --format '{{.Architecture}}'` reports `arm64`.
- [x] A single combined `docker buildx build --platform linux/amd64,linux/arm64 …` invocation completes successfully and its exported output demonstrably contains both `linux/amd64` and `linux/arm64`.
- [x] The `:amd64` and `:arm64` images have identical `Entrypoint` and `ExposedPorts` configuration (one image definition, two architectures).
- [x] Tasks 13/14 remain intact: `docker compose config` still succeeds, the Task 13 `cardrone-backend` image tag is still present, and validation used only the separate `:amd64`/`:arm64` tags.
- [x] No new dependencies: no project file (Dockerfile, `.dockerignore`, `.csproj`, `.sln`, compose, `.gitignore`) changed; `tonistiigi/binfmt` was used only as a throwaway validation tool.
- [x] `dotnet build backend/DroneControl.sln` still reports 0 warnings / 0 errors and `npm test` still reports 99/99 passing (frontend untouched).
- [x] Cleanup completes: no `cardrone-backend:amd64` or `cardrone-backend:arm64` tags remain, the OCI tarball is deleted, any temporary buildx builder is removed, and no validation containers are left running.
