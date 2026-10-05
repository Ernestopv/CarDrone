# Full Stack Docker Compose

## Purpose

Task 20 turns the backend-only container stack into the complete development environment:

```text
Docker Compose
├── frontend   (new: built React app served by nginx, /api/ proxied to the backend)
└── backend    (Task 14 service, unchanged)
```

After this task, `docker compose up` runs the entire application with one command, the frontend container reaches the backend through compose networking (service DNS, no host assumptions), and the four backlog validations — frontend loads, backend health responds, frontend communicates with backend, drone simulator works end-to-end — are demonstrable over plain HTTP without a browser or any application-code change.

## Dependencies

- `specs/docker/docker-compose-development.md` (Task 14) — the existing root `docker-compose.yml`: single `backend` service, build `./backend`, image `cardrone-backend`, `5080:8080`, `ASPNETCORE_ENVIRONMENT=Development`, and the bash `/dev/tcp` healthcheck. This task appends a `frontend` service and must not disturb the backend contract.
- `specs/docker/backend-container.md` (Task 13) — the backend image and its architecture-neutral Dockerfile; consumed unchanged.
- `specs/integration/frontend-api-service.md` — `ApiDroneService` reads `VITE_API_BASE_URL` via `import.meta.env` and **only falls back to `http://localhost:5080` when the variable is unset**; an explicitly empty value produces relative same-origin URLs. That existing behavior is the enabling mechanism for the nginx-proxy design (no code change here or in the frontend).
- `specs/integration/frontend-backend.md` — the app now defaults to api mode, so a composed stack that actually serves the backend is meaningful end-to-end.
- `frontend/package.json` + `package-lock.json` — `npm ci` + `npm run build` (`tsc -b && vite build`, default output `dist`) as the container build pipeline; reproducible via the lockfile.
- Docker skill — multi-stage builds, `.dockerignore`, orchestrator-level health probes, `depends_on` with health conditions.
- dotnet skill — backend service configuration stays Development-env-var driven (no code).

## Scope

- `frontend/Dockerfile`: multi-stage build (`node:22-alpine` builder running `npm ci` + `npm run build` with `package*.json` copied first for layer caching → `nginx:1-alpine` runtime serving `dist`).
- `frontend/nginx.conf`: static SPA serving with the SPA fallback and a reverse proxy of `/api/` to `http://backend:8080` (compose service DNS).
- `frontend/.dockerignore`: excludes `node_modules`, `dist`, `.env*`, and noise from the build context.
- Root `docker-compose.yml`: append a `frontend` service — build `./frontend`, image `cardrone-frontend`, host port `8081` → container `80`, `depends_on: backend: condition: service_healthy`, an nginx healthcheck using the image's built-in `wget`.
- Empty-string `VITE_API_BASE_URL` baked through a build ARG (default `''`) so the composed frontend talks same-origin through the proxy.
- `docs/DOCKER.md`: update the Compose quick-reference to the full stack (the file's commands all remain validated ones).

## Out of Scope

- Any change to application code, the frontend toolchain, `backend/Dockerfile`, `backend/.dockerignore`, or the backend service block (both remain byte-identical).
- Vite dev-server-in-container / HMR workflows (hot reload stays outside Docker via `npm run dev` against the composed backend, exactly as today; no source bind mounts).
- Registry pushes, deployment/production stacks, compose profiles, override files.
- Multi-arch builds (the stack runs on the host platform; image neutrality is Task 15's, Raspberry Pi deployment is a later phase).
- A uStreamer/camera service, Raspberry Pi anything, TLS, authentication, resource limits.
- nginx hardening beyond serving the SPA + proxy: the official nginx image keeps its default user setup (documented dev-only trade-off), no custom certs, no security headers engineering.
- Kubernetes/compose alternatives.

## Architecture

```text
                        docker compose up
                                │
        ┌───────────────────────┴───────────────────────┐
        ▼                                               ▼
 frontend (cardrone-frontend)                    backend (cardrone-backend)
 nginx :80  ← host 8081                          aspnet :8080 ← host 5080
 ├── /            → static dist (+ SPA fallback)  ├── /api/health
 └── /api/        → proxy_pass http://backend:8080 └── /api/drone/*
        ▲
        │ built with VITE_API_BASE_URL='' → the app fetches relative
        │ same-origin /api/… paths; the browser never needs to know
        │ the backend's host or port, and nothing inside a container
        │ resolves 'localhost'
```

Decisions embedded in the design:

1. **Static build + reverse proxy, not a dev-server container.** The composed frontend is `vite build` output served by nginx with `/api/` proxied to the backend service. This makes "frontend communicates with backend" a container-network property (no localhost assumptions, no CORS dependency, no baked host ports) with a small reproducible image, while the HMR loop deliberately stays host-side (`npm run dev` + the existing Development CORS path) — the backlog's "development environment" is the one-command stack, not a hot-reload-in-Docker claim.
2. **Zero application code changes.** The empty-string base URL already behaves as relative mode (the `??` fallback fires only on unset); `depends_on` + healthcheck give ordering; the nginx conf covers SPA routing. The composed stack exercises the identical contracts validated in Tasks 15/18/19.
3. **Backend service block untouched**; the frontend joins with its own healthcheck (nginx ships busybox `wget`, unlike aspnet), and `depends_on: service_healthy` so the proxy never starts against a dead upstream.
4. **Host ports stay convention**: backend keeps `5080:8080` (every prior validation, launchSettings, docs); frontend takes `8081:80` — chosen free, documented, and never referenced from inside any container.

## Behavior

One-command lifecycle:

1. `docker compose config` renders both services without error.
2. `docker compose up -d` from a clean `down`: builds any missing image (frontend: `npm ci` + build; backend: unchanged), starts backend → healthcheck flips it `healthy` (~10 s) → frontend then starts and flips `healthy` on its own probe; `docker compose ps` shows both `healthy`.
3. Browser/HTTP surface:
   - `GET http://localhost:8081/` → the app's `index.html` and its built `/assets/*` chunks (nginx static);
   - `GET http://localhost:8081/api/drone/status` → proxied to `backend:8080`, answers the wire status JSON;
   - the simulator flow (connect → command → speed → disconnect) over `:8081` behaves exactly like direct `:5080` calls (same backend process, same singleton state).
4. `GET http://localhost:5080/api/health` keeps answering directly (backend contract unchanged).
5. `docker compose down` removes both containers; images stay for reuse.
6. Restart behavior: `compose up` again reuses images; simulator state resets with the backend container (in-memory singleton — expected, documented in Task 11/13).

Failure expectations (environmental, reported not coded): Docker daemon down; port 5080/8081 occupied; registry pulls for `node:22-alpine`/`nginx:1-alpine` unavailable; `npm ci` network failure — none are product behavior changes.

## Interfaces

Compose service contract (new/changed lines only):

| Aspect | Value |
| --- | --- |
| Frontend image tag | `cardrone-frontend` (local, like `cardrone-backend`) |
| Frontend build | context `./frontend`, Dockerfile `Dockerfile`, build arg `VITE_API_BASE_URL` defaulting to empty string |
| Frontend host port | `8081` → container `80` |
| Inter-service URL | nginx proxy target `http://backend:8080` (compose DNS name, never localhost) |
| Frontend healthcheck | busybox `wget -qO- http://127.0.0.1/` based probe (tooling shipped in nginx:alpine), interval/timeout/retries mirroring backend cadence |
| Dependency | `frontend.depends_on.backend: condition: service_healthy` |
| Backend service | unchanged from Task 14 (build, image, `5080:8080`, Development env, `/dev/tcp` healthcheck) |

Developer-facing URLs: app `http://localhost:8081`, API (direct) `http://localhost:5080`, API (via frontend proxy) `http://localhost:8081/api/…`.

## Platform Requirements

- Docker Desktop with a running Linux-engine daemon and Compose v2 (observed: Docker 29.1.3, linux/x86_64).
- Host ports 5080 and 8081 free during validation.
- Network access for the two new base-image pulls and `npm ci` inside the builder stage.
- Node ≥ 20.19 requirement satisfied by `node:22-alpine` (matches Vite 7 engine needs); the host toolchain is untouched.
- Host-platform (amd64) builds only; ARM64 remains Task 15's image-level concern.

## Security / Safety

- Development-only stack: the backend keeps running `ASPNETCORE_ENVIRONMENT=Development` inside the container exactly as Task 14 specified; production deployment is out of scope.
- No secrets anywhere: compose file, Dockerfiles, and nginx conf contain only public URLs/ports; `.dockerignore` keeps any local `.env*` out of the image even if a developer creates one.
- The nginx proxy exposes the same public surface as direct backend access (paths under `/api/`), no admin or file endpoints.
- No devices, GPIO, or privileged runtime — the composed frontend/backend remain portable application code (AGENTS Docker/Pi boundary).
- Acknowledgement semantics unchanged through the proxy: responses are byte-identical to direct backend calls (simulated acks only).

## Testing Scenarios

Executed in order; all artifacts are the stack itself (nothing throwaway survives except documented state):

1. Inspection: Dockerfile multi-stage + npm-ci layer caching order (lockfiles copied before source), final stage contains only `dist` + nginx conf; `.dockerignore` patterns; compose diff limited to the appended frontend service (+ comment adjustments); zero application-source hash drift (frontend and backend).
2. `docker compose config` renders; grep proves no `localhost` inside frontend build/runtime config (Dockerfile, nginx.conf, compose frontend block).
3. Clean start: `docker compose down` → `docker compose up -d` (builds included) → `docker compose ps` shows `backend` and `frontend` both `healthy` within 90 s.
4. Frontend loads: `GET http://localhost:8081/` → 200 HTML containing a built `/assets/*.js` reference; `GET` that asset → 200.
5. Backend health: `GET http://localhost:5080/api/health` → 200 `{"status":"ok"}`.
6. Frontend ↔ backend + simulator E2E, all through `http://localhost:8081`: `GET /api/drone/status` → wire status (offline, `api:connected`); `POST /api/drone/connect` → 200 connected after pacing; `POST /api/drone/command {"command":"forward"}` → 200 `confirmedCommand:"forward"`; `PUT /api/drone/speed {"speed":45}` → 200 `speed:45`; `POST /api/drone/disconnect` → 200 offline state; error shape spot-check: command after disconnect → 409 `application/problem+json` through the proxy.
7. Regression: `dotnet build` 0/0, `dotnet test` 51/51, `npm test` 121/121 (no source touched by this task).
8. Teardown: `docker compose down` leaves no compose containers; documented URLs/ports match `docs/DOCKER.md` after its update.

## Acceptance Criteria

- [x] Created files are exactly `frontend/Dockerfile`, `frontend/nginx.conf`, and `frontend/.dockerignore`; modified are `docker-compose.yml` and `docs/DOCKER.md`; every application source file in `frontend/src/`, `backend/src/`, plus `backend/Dockerfile`, `backend/.dockerignore`, `frontend/package.json`, and `package-lock.json` is byte-identical (hash-verified) — no application-code change was required.
- [x] The frontend Dockerfile is multi-stage: a `node:22-alpine` builder copies `package.json`/`package-lock.json` before source, runs `npm ci`, then `npm run build`; the final `nginx:1-alpine` stage contains only the built `dist` output and the nginx config — no sources, no `node_modules`.
- [x] `docker-compose.yml` keeps the Task 14 backend service exactly as it was (build context, image tag, `5080:8080`, Development environment, `/dev/tcp` healthcheck) and appends a `frontend` service with image `cardrone-frontend`, host port `8081:80`, and `depends_on` the backend gated on `service_healthy`.
- [x] No `localhost` (or host port assumption) appears in `frontend/Dockerfile`, `frontend/nginx.conf`, or the compose `frontend` block: container-to-container reach uses `http://backend:8080`, and the frontend is built with an empty `VITE_API_BASE_URL` so the app issues relative same-origin `/api/…` requests.
- [x] `docker compose config` succeeds from the repository root; after `docker compose down` + `docker compose up -d` from that clean state, `docker compose ps` reports both `backend` and `frontend` as `healthy` within 90 seconds.
- [x] `GET http://localhost:8081/` returns HTTP 200 with the built `index.html` (referencing an `/assets/*.js` chunk that also serves 200) — "frontend loads".
- [x] `GET http://localhost:5080/api/health` returns 200 `{"status":"ok"}` — "backend health endpoint responds".
- [x] `GET http://localhost:8081/api/drone/status` returns the wire status JSON through the proxy — "frontend communicates with backend".
- [x] The full simulator flow over `http://localhost:8081` passes: connect → mapped connected, command `forward` → `confirmedCommand:"forward"`, speed 45 → `speed:45`, disconnect → offline; a command after disconnect → 409 `application/problem+json` — "drone simulator works end-to-end".
- [x] `frontend/.dockerignore` excludes `node_modules`, `dist`, and `.env*`, keeping local artifacts and any future env file out of the build context.
- [x] `docs/DOCKER.md`'s Compose section documents the full stack (both services, URLs, one-command usage) and every command it lists was executed successfully during validation.
- [x] After validation, `docker compose down` leaves no compose containers; `dotnet build` 0 warnings/0 errors with all 51 backend tests passing, and `npm test` 121/121 (the task introduced no code drift).
