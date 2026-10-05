# Docker Compose Development Environment

## Purpose

Task 14 gives the project a one-command local container environment: a root `docker-compose.yml` that starts the backend image from Task 13 with `docker compose up`, maps it to the host, configures the Development environment, and supplies the orchestrator-level health probe deliberately deferred from Task 13 (the Dockerfile carries no `HEALTHCHECK` and the image ships no HTTP client tooling). The frontend deliberately continues to run outside Docker.

## Dependencies

- `specs/docker/backend-container.md` — supplies the image: multi-stage `backend/Dockerfile`, local tag `cardrone-backend`, container port 8080, non-root `USER app`, and the documented absence of any `HEALTHCHECK` instruction (Task 14 owns probe orchestration).
- `specs/backend/drone-api.md` — `GET /api/health` is the probe target and the host-facing smoke test; `/openapi/v1.json` exists only in Development and is therefore the observable proof that the compose-configured environment variable reached the application.
- `specs/backend/backend-setup.md` — the 5080 host-port convention the mapping preserves.
- Docker skill — Compose health checks belong to the orchestrator, not the Dockerfile; secrets flow through environment values, never literals in files or images.
- `tasks/BACKLOG.md` Task 14 — root file location, single-service initial architecture, and the five requirements (start via `docker compose up`, map the port, development environment variables, health check where useful, keep secrets out of source control).

## Scope

- Root `docker-compose.yml` (location mandated by the backlog): one `backend` service.
- A build reference to the existing `backend/Dockerfile` — no second Dockerfile is created.
- Host port mapping and the `ASPNETCORE_ENVIRONMENT` Development setting.
- A Compose `healthcheck` on the `backend` service (the "health check where useful" requirement).
- Secrets hygiene: no secret literals in the compose file, plus a root `.gitignore` excluding `.env` (Compose's default interpolation file) so future sensitive configuration never enters version control.
- Validation by `docker compose config`, `build`, `up`, health state, host HTTP checks, `exec`, `down`, plus host build and frontend regression.

## Out of Scope

- A `frontend` service — the backlog states the frontend may initially continue outside Docker.
- Additional services (databases, caches, uStreamer, Raspberry Pi) — the initial architecture is `Docker Compose └── backend`, and a single-service graph has nothing to `depends_on`.
- Task 15 concerns: `platform`, buildx, multi-arch targets — this task runs the host's default platform only.
- Production concerns: TLS, reverse proxy, restart policies, compose profiles/override files, registry pushes.
- Source bind mounts and hot reload (the container runs published output, not a development loop).
- Any change to `backend/Dockerfile`, `backend/.dockerignore`, or application source — the health probe lives in Compose, preserving Task 13's no-`HEALTHCHECK` Dockerfile decision.
- Docker secrets/vault tooling — there are no secrets to manage yet; only the no-literals rule applies.
- Volumes/persistence — the simulator state is in-memory and resets on every restart (parity with Task 13).

## Architecture

```text
repository root
├── docker-compose.yml   (new — backlog-mandated location)
├── .gitignore           (new — excludes .env)
└── backend/
    ├── Dockerfile       (Task 13 — reused unchanged)
    └── .dockerignore
```

```text
docker compose up
      │
      ▼
build ./backend ──► image cardrone-backend (Task 13 multi-stage, unchanged)
      │
      ▼
container: USER app, listens on 8080, ASPNETCORE_ENVIRONMENT=Development
      │ ports: 5080:8080
      ├────────────► host GET :5080/api/health, GET :5080/openapi/v1.json
      │
      └── healthcheck (executed inside the container, orchestrated by Compose)
            HTTP GET /api/health via bash /dev/tcp — the only HTTP-capable
            tooling in aspnet:10.0 (sh/bash/grep present, curl/wget absent;
            verified against the published image)
```

Decisions embedded in the design:

- **One command, self-contained**: `build: {context: ./backend}` together with `image: cardrone-backend` means `docker compose up` works on a machine that has never built the image, and tags the result consistently with Task 13's manual builds.
- **Probe location and mechanism**: the probe is a Compose `healthcheck`, never a Dockerfile `HEALTHCHECK`. The `aspnet:10.0` image has a shell but no HTTP client (verified: `/bin/sh` and `/usr/bin/bash` with working `/dev/tcp` redirections exist; `curl` and `wget` do not), so the test performs a raw HTTP GET against `/api/health` with bash `/dev/tcp` and requires a `200` status line. This needs no image modification and no package installation.
- **No `depends_on`**: with one service there is nothing to gate on health; `condition: service_healthy` is reserved for a future second service.
- **Project-name independence**: all validation uses Compose commands (`up`, `build`, `ps`, `exec`, `logs`, `down`) run from the repository root, so it does not depend on the folder name from which Compose derives the project name.

## Interfaces

The compose file is the interface artifact:

| Aspect | Contract |
| --- | --- |
| Service | exactly one service, named `backend` |
| Build | context `./backend`, Dockerfile `Dockerfile`, image tag `cardrone-backend` |
| Ports | host `5080` → container `8080` |
| Environment | `ASPNETCORE_ENVIRONMENT=Development` |
| User | image default `app` (non-root; compose must not override it) |
| Healthcheck | in-container HTTP GET `/api/health` expecting status 200, with `interval`, `timeout`, and `retries` configured |
| Health visibility | `docker compose ps` reports the service `healthy` |

## Behavior

Expected lifecycle:

1. `docker compose config` renders without error.
2. `docker compose build` builds the image through Compose (reusing Task 13's cached layers); `docker compose up -d` (or foreground `up`) starts the backend, and the healthcheck moves it from `starting` to `healthy` within 60 seconds under normal conditions.
3. While running: host requests to `http://localhost:5080/api/health` return 200 `{"status":"ok"}`; `http://localhost:5080/openapi/v1.json` returns 200 because the compose file set Development (Task 13 proved it is 404 under the image's Production default).
4. `docker compose exec backend whoami` reports `app` — Compose runs the non-root image as-is.
5. `docker compose down` stops and removes the project's containers and default network; a subsequent `up` recreates them from scratch, resetting the in-memory simulator state.
6. `docker compose up --build` re-runs the build after Dockerfile changes (plain `up` reuses an existing image).

Environmental failures are reported, not spec behavior: the daemon must be running with a Linux engine, host port 5080 must be free while the stack runs, and the Compose v2 plugin (`docker compose`) is required — the legacy `docker-compose` binary is not assumed.

## Platform Requirements

- Docker engine with the Compose **v2 plugin** (`docker compose ...`).
- Linux-engine daemon running (observed at planning: Docker CLI 29.1.3, engine linux/x86_64).
- Host port 5080 free while the stack runs.
- Probe-tooling facts, verified against `mcr.microsoft.com/dotnet/aspnet:10.0`: `/bin/sh`, `/usr/bin/bash` with working `/dev/tcp` redirections, and `grep` are present; `curl` and `wget` are absent. The healthcheck may rely only on this toolset (installing anything into the image is out of scope).
- Single host platform — multi-arch is Task 15.

## Security / Safety

- The compose file contains no secret values (the application has none); future sensitive values must flow through environment interpolation (`${VAR}`) read from an untracked `.env`, and the root `.gitignore` excludes `.env`.
- The service runs Task 13's non-root image unchanged: no `user:` override to root, no `privileged:`, no device mappings — the container keeps zero access to hardware or GPIO.
- `ASPNETCORE_ENVIRONMENT=Development` is a local-development configuration for this development file only; production deployment is out of scope.
- Simulated acknowledgements only: the API inside the container still reports simulated state, never hardware confirmation.

## Testing Scenarios

Executed in order (throwaway — all containers removed afterward; no test project is created):

1. Source inspection: exactly one `backend` service; build references `./backend`; port and environment entries present; `healthcheck` present and containing `/api/health` with interval/timeout/retries; no secret-like literals; no `user: root`, `privileged:`, or `devices:`; no `frontend` service; `backend/Dockerfile`, `backend/.dockerignore`, and application files unmodified.
2. `docker compose config` succeeds from the repository root.
3. `docker compose build` succeeds (the declared build path genuinely builds through Compose).
4. Clean start: `docker compose down` clears leftovers → `docker compose up -d` → poll `docker compose ps` until the backend reports `healthy` (≤60 s).
5. Host checks: `GET http://localhost:5080/api/health` → 200 `{"status":"ok"}`; `GET http://localhost:5080/openapi/v1.json` → 200 (Development configured by Compose).
6. Non-root inside Compose: `docker compose exec backend whoami` → `app`.
7. Health detail: `docker inspect` of the running compose container shows health `Status: healthy` with the configured test recorded.
8. Teardown: `docker compose down` → `docker compose ps -a` shows no containers.
9. Regression: `dotnet build backend/DroneControl.sln` → 0 warnings / 0 errors; `npm test` → 99/99; file inventory shows only `docker-compose.yml` and `.gitignore` added.

## Acceptance Criteria

- [x] A root `docker-compose.yml` exists; the only other file created is a root `.gitignore`; no application source, `backend/Dockerfile`, or `backend/.dockerignore` file is modified.
- [x] The compose file defines exactly one service, named `backend`, with `build.context` `./backend` and image tag `cardrone-backend` (reuses the Task 13 Dockerfile; no second Dockerfile exists anywhere in the repository).
- [x] `docker compose config` completes without error from the repository root.
- [x] `docker compose build` completes successfully (the declared build path actually builds through Compose).
- [x] The service maps host port `5080` to container port `8080`.
- [x] The service sets `ASPNETCORE_ENVIRONMENT=Development`.
- [x] The service defines a Compose `healthcheck` whose test targets `GET /api/health` inside the container and configures `interval`, `timeout`, and `retries`.
- [x] The healthcheck test uses only tooling present in `aspnet:10.0` (shell/bash/`/dev/tcp`/`grep`) — it requires no change to the Dockerfile and installs nothing into the image.
- [x] After `docker compose down` followed by `docker compose up -d`, the backend service reaches state `healthy` in `docker compose ps` within 60 seconds.
- [x] While the stack is running, `GET http://localhost:5080/api/health` from the host returns HTTP 200 `{"status":"ok"}`.
- [x] While the stack is running, `GET http://localhost:5080/openapi/v1.json` from the host returns HTTP 200, proving the Development environment variable configured by Compose reached the application.
- [x] `docker compose exec backend whoami` reports `app` (Compose does not escalate to root).
- [x] The compose file contains no secret values, and a root `.gitignore` exists that excludes `.env`.
- [x] The compose file contains no `privileged` mode, no device mappings, and no service other than `backend`.
- [x] `docker compose down` removes every container created by the project (subsequent `docker compose ps -a` shows none).
- [x] `dotnet build backend/DroneControl.sln` still reports 0 warnings / 0 errors and `npm test` still reports 99/99 passing (frontend untouched).
