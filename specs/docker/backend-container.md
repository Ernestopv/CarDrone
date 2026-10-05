# Backend Container

## Purpose

Task 13 packages the existing ASP.NET Core backend into a portable Docker image: a multi-stage `Dockerfile` that builds inside the official .NET SDK image and runs on the slim ASP.NET runtime image, plus a `.dockerignore` that keeps local build artifacts out of the build context. Validation is `docker build`, `docker run`, and `GET /api/health` from the running container — proving the backend runs identically inside a container without any application-code changes.

The image is deliberately architecture-neutral and hardware-free: it contains only portable application code, preparing (but not performing) the multi-architecture builds of Task 15 and the Compose environment of Task 14.

## Dependencies

- `specs/backend/backend-setup.md` — solution layout the build must package: `backend/DroneControl.sln` with `src/` containing the four projects (Api is the only runnable entry point).
- `specs/backend/drone-api.md` — the endpoints available for validation, especially `GET /api/health` (works in every environment) and the Development-only OpenAPI document used to observe environment configuration.
- `specs/backend/drone-simulator.md` — what the container actually runs: the in-memory simulator, no hardware, no network I/O (the image therefore needs no device access whatsoever).
- `tasks/BACKLOG.md` Task 13 requirements: multi-stage build, official .NET images, build in container, runtime-only final image, exposed HTTP port, environment-variable configuration, hardware independence.
- Docker skill: multi-stage pattern, csproj-first restore layering, non-root `app` user, orchestrator-level health probes (no in-image `HEALTHCHECK`).

## Scope

- `backend/Dockerfile` — multi-stage build (SDK build stage, ASP.NET runtime stage).
- `backend/.dockerignore` — excludes local build outputs from the build context.
- Validation by building the image, running it, and verifying endpoints from the host; source inspection of the Dockerfile/.dockerignore; host regression build.

## Out of Scope

- `docker-compose.yml`, port-mapping files, Compose health checks, secrets management files — Task 14 (root-level `docker-compose.yml`).
- Buildx, `linux/amd64`/`linux/arm64` targets, image documentation — Task 15. This task builds only the host's default platform.
- Any application source change: no new endpoints, no `/health/live` addition (the existing `GET /api/health` is the probe target), no `Program.cs` or `.csproj` edits.
- Registry pushes, CI pipelines, image scanning/signing, versioning strategy.
- Frontend containerization.
- Image-size optimizations not required here (Alpine/chiseled variants, trimming, AOT) — default Debian-based variants keep Task 15's multi-arch story simple.
- GPIO, device mounts, `--device` flags, or any hardware access from the container — the image is portable application code only.

## Architecture

```text
backend/
├── DroneControl.sln          (build context root — unchanged)
├── Dockerfile                (new: multi-stage)
├── .dockerignore             (new)
└── src/ ...                  (unchanged application code)
```

Build stage:

1. Base: `mcr.microsoft.com/dotnet/sdk:10.0` (official SDK image, multi-arch manifest — reusable by Task 15).
2. Copy the four `.csproj` files preserving their `src/<Project>/` paths and run `dotnet restore` for `src/DroneControl.Api/DroneControl.Api.csproj` **before** copying source — this caches the NuGet layer so source edits do not re-download packages.
3. Copy the remaining source and `dotnet publish` the Api project (`-c Release`, output to a publish folder, `--no-restore`).
4. Publish is **framework-dependent with no runtime identifier pinned** — no self-contained build, no `--platform` instruction, so the same Dockerfile builds for amd64 now and arm64 in Task 15.

Runtime stage:

1. Base: `mcr.microsoft.com/dotnet/aspnet:10.0` — the final image is never the SDK image.
2. Copy only the publish output from the build stage.
3. Run as the built-in non-root user (`USER app`, available in .NET 8+ images).
4. `EXPOSE 8080` — the .NET container images default to listening on `ASPNETCORE_HTTP_PORTS=8080`.
5. `ENTRYPOINT ["dotnet", "DroneControl.Api.dll"]`.

`.dockerignore` excludes `**/bin`, `**/obj`, `**/.vs`, Docker/Compose files, and VCS metadata from the context, so local Debug outputs never leak into the image layers.

Boundary rule: containerization stays in `backend/Dockerfile` only — nothing container-related enters Domain, Application, or Infrastructure code (architecture rule: containerization must not leak into Domain logic).

## Behavior

Container contract:

| Aspect | Value |
| --- | --- |
| Image tag | `cardrone-backend` (local validation tag) |
| Container port | `8080` (default ASP.NET container port) |
| Host mapping (validation) | `5080:8080` — consistent with the project's 5080 development port |
| Default environment | `Production` (image default; no env baked in) |
| Configuration | Standard ASP.NET Core environment variables at run time, e.g. `docker run -e ASPNETCORE_ENVIRONMENT=Development` |
| Process user | non-root `app` |
| Logs | stdout/stderr, readable via `docker logs` |
| Health surface | existing `GET /api/health` from the host through the mapped port |
| State | in-memory simulator only — container restart resets drone state (expected; no persistence exists by design) |

Environment-variable configuration is observable: `/openapi/v1.json` is mapped only in the Development environment, so it returns 404 by default in the container and 200 when `ASPNETCORE_ENVIRONMENT=Development` is passed — an objective proof that `docker run -e` reaches application configuration.

There is deliberately no `HEALTHCHECK` instruction: the standard `aspnet` image ships no shell/curl/wget for an in-image probe, and probe orchestration (against `/api/health`) is Task 14's concern.

## Platform Requirements

- Docker CLI with a **running Linux-engine daemon** (Docker Desktop on Windows, Linux containers mode). Observed at planning time: Docker CLI 29.1.3 installed, daemon stopped — starting Docker Desktop is an implementation prerequisite; if the daemon cannot start, the task is blocked, not failed.
- Network access during build: pull `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0`, plus NuGet restore inside the build stage.
- Windows containers mode is not supported (the .NET images targeted here are Linux images).
- Host `dotnet build` must keep passing — Docker work introduces no source changes.

## Security / Safety

- The container runs as non-root `app`.
- No secrets, connection strings, or credentials exist in the Dockerfile, the build context, or required run-time configuration (the app has none; configuration flows through environment variables — Task 14 keeps secrets out of source control).
- No device/GPIO access: the image runs portable application code only; the simulator performs no hardware I/O, so nothing in the container can touch drone hardware (Docker tasks must not introduce direct hardware access; ARM64 portability later does not imply GPIO access either).
- Identical response semantics inside the container: the API still reports simulated acknowledgements, never hardware confirmation.

## Testing Scenarios

Executed in order (throwaway — containers and validation runs are cleaned up afterward; no test project is created):

1. Source inspection: Dockerfile structure (stages, layer order, non-root, entrypoint, no `HEALTHCHECK`, no `--platform`), `.dockerignore` patterns, no application files modified.
2. Build: `docker build -f backend/Dockerfile -t cardrone-backend backend/` succeeds on the host's default platform.
3. Run (default): `docker run -d --name <name> -p 5080:8080 cardrone-backend` → from the host, `GET http://localhost:5080/api/health` → 200 `{"status":"ok"}`; `GET /openapi/v1.json` → 404 (Production default).
4. Non-root: `docker exec <name> whoami` → `app`.
5. Environment configuration: remove the container; re-run with `-e ASPNETCORE_ENVIRONMENT=Development` (mapped to a host port) → `GET /openapi/v1.json` → 200, proving run-time environment configuration reaches the app; `GET /api/health` still 200.
6. Cleanup: `docker rm -f` the validation containers (the local image tag may remain).
7. Host regression: `dotnet build backend/DroneControl.sln` → 0 warnings / 0 errors; frontend untouched.

## Acceptance Criteria

- [ ] `backend/Dockerfile` and `backend/.dockerignore` exist; no other files are created and no application source file (`.cs`, `.csproj`, `.sln`, `Program.cs`) is modified by this task.
- [ ] The Dockerfile is multi-stage: a build stage based on `mcr.microsoft.com/dotnet/sdk:10.0` and a final runtime stage based on `mcr.microsoft.com/dotnet/aspnet:10.0`; the final image is not an SDK image.
- [ ] The four `.csproj` files are copied and `dotnet restore` runs before the full source is copied (cached NuGet layer); publish runs in Release mode with `--no-restore` for `DroneControl.Api`.
- [ ] The runtime stage sets `USER app` (non-root) before the entry point executes.
- [ ] The runtime stage declares `EXPOSE 8080` and `ENTRYPOINT ["dotnet", "DroneControl.Api.dll"]`.
- [ ] The Dockerfile contains no `HEALTHCHECK` instruction, no `--platform` pin, and no self-contained/RID-specific publish (architecture-neutral for Task 15).
- [ ] No secrets, connection strings, GPIO/pin references, or `--device` usage appear in the Dockerfile or `.dockerignore`.
- [ ] `.dockerignore` excludes `**/bin`, `**/obj`, and `**/.vs` from the build context (local Debug artifacts never enter image layers).
- [ ] `docker build -f backend/Dockerfile -t cardrone-backend backend/` succeeds.
- [ ] A container started with `docker run -d -p 5080:8080 cardrone-backend` serves `GET http://localhost:5080/api/health` → HTTP 200 `{"status":"ok"}` from the host (backlog validation).
- [ ] With no extra environment variables, `GET /openapi/v1.json` inside the container → 404 (Production default) while health still returns 200.
- [ ] Re-running the image with `-e ASPNETCORE_ENVIRONMENT=Development` makes `GET /openapi/v1.json` return HTTP 200 — run-time configuration via environment variables is observably effective.
- [ ] `docker exec <container> whoami` reports `app` (non-root verified at runtime, not just in source).
- [ ] Validation containers are removed after the run (`docker rm -f`), and `dotnet build backend/DroneControl.sln` still succeeds with 0 warnings and 0 errors; the frontend is untouched.
