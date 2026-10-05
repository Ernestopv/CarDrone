# Docker Build Commands

Validated commands for building and running the CarDrone backend container.
Every command in this file was executed successfully during Task 15 validation
(`specs/docker/multi-arch.md`). Run them from the repository root.

## Cross-architecture prerequisite

Building for a non-host architecture (for example `linux/arm64` on a
`linux/amd64` host) requires QEMU/binfmt emulation. Register it with a
throwaway privileged container:

```bash
docker run --privileged --rm tonistiigi/binfmt --install arm64
```

Verify the registration:

```bash
docker buildx ls
docker run --rm --platform linux/arm64 alpine uname -m
```

`docker buildx ls` must list `linux/arm64` and the probe must print
`aarch64`. Re-run the registration after a Docker Desktop VM restart if
cross-builds fail with `exec format error`.

## Single-architecture build (host platform)

```bash
docker build -f backend/Dockerfile -t cardrone-backend backend/
```

## Per-architecture buildx builds (loaded into the local image store)

```bash
docker buildx build --platform linux/amd64 -t cardrone-backend:amd64 --load backend/
docker buildx build --platform linux/arm64 -t cardrone-backend:arm64 --load backend/
```

Verify each image:

```bash
docker image inspect cardrone-backend:amd64 --format '{{.Architecture}}'
docker image inspect cardrone-backend:arm64 --format '{{.Architecture}}'
```

## Combined multi-architecture build (single invocation, no registry)

The `docker` driver cannot write OCI output, so use a dedicated
`docker-container` builder. The three commands below form a re-runnable
sequence: `create --use` selects the new builder, the build runs on it, and
`docker buildx rm --force` removes the currently selected builder without an
interactive confirmation prompt.

```bash
docker buildx create --driver docker-container --use
docker buildx build --platform linux/amd64,linux/arm64 --output type=oci,dest=cardrone-multiarch.tar -f backend/Dockerfile backend/
docker buildx rm --force
```

The resulting `cardrone-multiarch.tar` is an OCI image layout containing both
`linux/amd64` and `linux/arm64` (extract with `tar -xf` and check the
`"architecture"` field of the config blobs under `blobs/sha256`).

## Docker Compose (full-stack development environment)

One command builds and starts both services. The frontend serves the built
app and proxies `/api/` to the backend inside the compose network, so no
container references any host name:

```bash
docker compose build
docker compose config
docker compose up -d
docker compose ps
docker compose down
```

`up -d` starts existing images; run `docker compose build` (or `up -d --build`)
after source changes so the images carry the current code.

Browser entry point: `http://localhost:8081` (frontend; its `/api/` calls are
proxied). Direct API: `http://localhost:5080` — health at `/api/health`.
Backend-only startup from Task 14 still works (`docker compose up -d backend`).
For hot-reload frontend development, keep running `npm run dev` on the host
against the composed backend (the Development CORS policy allows it); set
`VITE_DRONE_SERVICE=mock` to work without any backend at all.

### Raspberry Pi hardware configuration boundary (Task 24)

The default PC stack remains device-free (`HARDWARE_MODE=mock`). The Pi-only
Compose overlay merges the same `backend` service (no second backend service):
copy `.env.raspberry.example` to the root `.env` on the Pi, then run the same
`docker compose up` command. Before using the overlay, complete
[`docs/hardware/raspberry-pi-inventory.md`](hardware/raspberry-pi-inventory.md)
and add only inventory-verified `devices`/`group_add` entries under the
`backend` service in `docker-compose.raspberry.yml`. Those entries are
intentionally absent until verified; there are no placeholder device paths or
group IDs. Validate the merged configuration on the Pi with:

```bash
docker compose config
```

The example selects `HARDWARE_MODE=real`, but Task 23's Raspberry provider is
still inert: this configuration boundary alone does not enable GPIO/PWM
operations or prove Pi runtime access. Pi device permissions and runtime
verification remain `NOT VERIFIED` until tested on the target.

Real mode also remains software-faulted until
`SAFETY_EXTERNAL_FAILURE_PROTECTION_VERIFIED=true` is set in the Pi `.env`.
Set it only after the external mechanism for abrupt process/power failure has
been separately verified and documented. The variable is an explicit operator
assertion, not a hardware check performed by Compose or the application.

### Hardware dry-run mode (Task 27)

`HARDWARE_MODE=dry-run` selects the same backend image and the same
hardware-backed command flow as `real`, but every intended GPIO/PWM operation
is logged and suppressed at an Infrastructure output sink — no physical sink
is registered in that graph, and no additional devices or privileges are
needed beyond the real path. On any non-`linux/arm64` runtime it aborts
startup with an explicit message (no mock fallback); missing/invalid `GPIO`
configuration aborts the same way. Dry-run suppression has been exercised on
development-PC tests and an emulated `linux/arm64` container only — the
actual Raspberry Pi dry-run runtime remains `NOT VERIFIED`.

### Camera / uStreamer container (Task 32)

The media plane serves the Pi camera's MJPEG stream to the browser at the
relative, same-origin path `/camera/` (`specs/docker/ustreamer-container.md`).

Build the uStreamer image (tag v6.67, non-root, internal port 8080):

```bash
docker buildx build --platform linux/arm64 -t cardrone-camera --load camera/
```

Run the camera stack on the Pi using the canonical `.env` flow (Task 34):
copy `.env.raspberry.example` to the root `.env` after the inventory evidence
exists. The example sets `COMPOSE_PROFILES=camera`, so the identical command
starts all three services:

```bash
docker compose up -d
```

(`docker compose --profile camera up -d` remains an ad-hoc equivalent.)

`CAMERA_PROXY_TARGET` (frontend env, empty by default) decides the nginx
`/camera/` behavior: empty → `404` (never the SPA fallback); set to
`http://ustreamer:8080/stream` → byte-for-byte MJPEG passthrough. Encoder
defaults are `CAMERA_RESOLUTION=1280x720`, `CAMERA_FPS=15`,
`CAMERA_QUALITY=80`. Real on-Pi video and camera device access remain
`NOT VERIFIED` (Tasks 36/38).

### Unified run flows (Task 34; `specs/docker/unified-compose.md`)

One source tree, one literal command:

- **PC (defaults only):** `docker compose up` — frontend + backend with the
  simulated camera; no `.env` is required and no camera container is created.
- **Raspberry Pi:** complete `docs/hardware/raspberry-pi-inventory.md`, copy
  `.env.raspberry.example` to the root `.env` (`HARDWARE_MODE=real` or
  `dry-run`, `CAMERA_MODE=ustreamer`, `COMPOSE_PROFILES=camera`,
  `CAMERA_PROBE_URL=http://frontend/camera/`,
  `CAMERA_PROXY_TARGET=http://ustreamer:8080/stream`), then the same
  `docker compose up`.

Applying the Pi `.env` on a non-`linux/arm64` host aborts backend startup
loudly (no mock fallback). The full Pi-stack runtime remains `NOT VERIFIED`
(Task 36).
