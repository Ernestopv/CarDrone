# Decision Records

ADR-style records of durable architectural decisions. Design decisions D1–D5
originate in `specs/architecture/runtime-deployment.md` (Task 21).

Status legend: `accepted (design only)` = decided and binding, but its
implementation is owned by later tasks and has not been built or run.

---

## D1 — No separate remote Raspberry Pi service

- **Status:** accepted (design only) — implementation owned by Tasks 22–24/34
- **Context:** The backend must control hardware on a Raspberry Pi. One option
  was a PC-hosted API talking over the network to a Pi-side agent service.
- **Decision:** The same ASP.NET Core backend runs *on the Pi* and accesses
  hardware locally through abstractions. No second drone-control service.
- **Consequences:** One contract stack, no network hop inside the control
  path, fewer failure modes, and the Task 15 ARM64-verified image lineage is
  reused as-is. Costs: the Pi hosts the whole backend runtime; hardware
  dependencies stay isolated behind Infrastructure so the PC build never
  carries them.
- **Revisit when:** a hardware-phase task demonstrates a concrete technical
  requirement for a split (none expected per the backlog constraint).

## D2 — One compose command; platform differences via deployment configuration

- **Status:** accepted — final form implemented (Task 34); Pi runtime verified (Task 36, 2026-10-05)
- **Context:** "Same folder, `docker compose up`" on PC and Pi. Docker Compose
  cannot conditionally omit individual entries from one service's `devices` or
  `group_add` lists based on an environment variable. Conventional override
  files loaded on every platform would also make the PC depend on Pi config.
- **Decision:** The literal command remains `docker compose up`; platform
  behavior is selected by the folder's untracked `.env`. PC uses the base
  `docker-compose.yml`. Task 24 documents the narrow device-mapping exception:
  on Pi, `.env` sets `COMPOSE_FILE=docker-compose.yml:docker-compose.raspberry.yml`
  so a Pi-only overlay merges into the SAME `backend` service. Device/group
  values are added to that overlay only after target inventory evidence exists.
- **Consequences:** PC defaults stay device-free; no fake device, second
  running backend, or source-code branch is introduced. The Pi overlay is
  selected only by the Pi `.env`, unlike an always-loaded conventional override.
- **Revisit when:** target Pi inventory and Task 34's final deployment validate
  the compose merge and profile strategy.

## D3 — Runtime mode is selected at the DI composition root, nowhere else

- **Status:** accepted — implemented (Tasks 23/31: composition-root selection for `HARDWARE_MODE`/`CAMERA_MODE`)
- **Context:** `mock | dry-run | real` hardware modes and `mock | ustreamer`
  camera modes must not leak as conditionals through business logic.
- **Decision:** `Program.cs`-level registration reads validated options and
  wires exactly one implementation graph. `IDroneController` remains the
  application contract; `IDroneHardware` (Task 22) is consumed *below* it by
  Pi-side implementations. Domain and Application never branch on platform.
- **Consequences:** Testability is structural (swap seams in tests); hardware
  libraries stay an Infrastructure concern; misuse fails at startup, not
  mid-request.
- **Revisit when:** Task 22 discovers the seam placement needs adjustment
  (contract moves only within this rule).

## D4 — Control plane and media plane never merge

- **Status:** accepted — media side implemented (Tasks 31–33); Pi runtime verified (Task 36, 2026-10-05)
- **Context:** Drone commands are request/response JSON; video is a continuous
  stream. Mixing them would couple the critical control path to streaming.
- **Decision:** `/api/` stays exactly today's ASP.NET contract (five endpoints
  + health, unchanged wire shape). Video goes to the reserved `/camera/` path
  via nginx→uStreamer only when `CAMERA_MODE=ustreamer`. The backend never
  proxies video; uStreamer never receives commands. Camera *status* still
  arrives through the existing `DroneStatus.camera` field.
- **Consequences:** Stream failures cannot take down control; the proven
  same-origin proxy pattern (Task 20) extends to a second location; frontend
  camera integration (Task 33) consumes a URL, not a new protocol.
- **Revisit when:** a camera task proves a different transport is required —
  the split itself is not negotiable, the mechanics may change.

## D5 — Strict config, fail fast, no silent fallback

- **Status:** accepted — enforced (Task 23 `HARDWARE_MODE`, Task 27 dry-run, Task 31 `CAMERA_MODE`, Task 34 deployment wiring)
- **Context:** A drone operator choosing `real` hardware mode (or a CI pipeline
  choosing `mock`) must never be silently served the other. AGENTS honesty
  rules forbid claiming hardware operations without real confirmation.
- **Decision:** Unknown/invalid `HARDWARE_MODE`/`CAMERA_MODE` values abort
  startup with explicit errors; `real`/`dry-run` on an unsupported platform
  aborts at provider preflight (Task 23); `dry-run` logs physical operations
  and never actuates (Task 27); simulated acknowledgements remain labeled
  `simulated` everywhere until a future wire extension (prerequisites ledger)
  carries real confirmation.
- **Consequences:** Misconfiguration fails loudly at the safest moment
  (startup); behaviour never differs silently from operator intent; the
  hardware-source ack contract gap is explicit, not hidden in code.
- **Revisit when:** never weakened — a future decision would have to *replace*
  it with a stronger safety property, and would need its own record.

## D6 — Select a Pi-only Compose overlay through the Pi `.env`

- **Status:** accepted — boundary implemented (Task 24), unified compose finalized (Task 34); device/group values applied from the target inventory (Task 36, 2026-10-05)
- **Context:** Compose cannot conditionally omit individual `devices` or
  `group_add` entries from one service based on an environment value. The PC
  stack must remain device-free, and Pi-specific mappings cannot be guessed.
- **Decision:** Keep the PC `docker-compose.yml` unchanged and device-free.
  On a Pi, `.env` sets `COMPOSE_FILE=docker-compose.yml:docker-compose.raspberry.yml`
  so the overlay merges configuration into the existing `backend` service.
  The overlay contains no mappings until the inventory document identifies
  verified nodes and minimum groups. Both platforms use `docker compose up`;
  no second backend/control service is created.
- **Consequences:** The checked-in PC path needs no Pi hardware or `.env`.
  The Pi `.env` selects the overlay; target inventory later supplies the exact
  `devices`/`group_add` entries. This is a narrow exception to D2's
  single-file preference: it avoids D2's rejected always-applied override
  because the Pi `.env` selects the second file only on the Pi.
- **Revisit when:** Task 24 inventory exists and the merged Pi config is
  rendered/tested on the target; any Compose limitation discovered there must
  preserve one active backend and least privilege.

## D7 — Fail-safe gates real mode on external abrupt-failure protection

- **Status:** accepted — software gate implemented by Task 26. Asserted for the
  bench bring-up sessions (2026-10-05) with the operator present (wheels up /
  power cut-off at hand); a documented external abrupt-failure mechanism
  remains NOT VERIFIED.
- **Context:** .NET shutdown handlers cannot guarantee STOP after a process
  crash, forced termination, kernel failure, Pi reboot, or power loss. No
  watchdog/electrical default has been verified in the project yet.
- **Decision:** `Safety:ExternalAbruptFailureProtectionVerified` defaults to
  `false`. In `HARDWARE_MODE=real`, the safety coordinator remains faulted and
  issues no controller operations unless an operator explicitly sets the flag
  after the external mechanism has been independently verified and documented.
  Setting the flag is an assertion, not proof; no GPIO or watchdog is invented.
- **Consequences:** The Pi software can be built and exercised through fakes,
  but real mode cannot actuate by default. Startup/shutdown software STOP
  requests remain best-effort and are never called physical confirmation.
- **Revisit when:** a later hardware/runtime task records the actual external
  mechanism and its Pi-runtime/real-hardware evidence; only then may the
  deployment configuration enable the flag.

## D8 — Real PWM speed in the container via a bind mount outside /sys

- **Status:** accepted and implemented — verified on the target (2026-10-05)
- **Context:** Task 36 expected `GPIO/PWM REAL` from `docker compose up`. GPIO
  direction works from the non-root container through the GPIO character device
  (`/dev/gpiochip0`). PWM is exposed only through sysfs
  (`/sys/class/pwm/pwmchipN/…`). Docker mounts `/sys` read-only, and a bind mount
  whose *target* is inside `/sys` (e.g. `/sys/class/pwm/pwmchip0`) is also
  read-only — the earlier "the chip tree cannot be written from a container"
  conclusion came from exactly that target. Mounting the same host tree at a
  path **outside** `/sys` (verified `-v /sys/class/pwm/pwmchip0:/pwm`) is
  writable by the non-root app user (the host files are `a+rw` via the
  `cardrone-pwm.service`).
- **Decision:** PWM is available in **both** runtimes. The native launcher
  (`scripts/run-native-pi.sh`) runs the stack on the host; the Docker Compose Pi
  overlay (`docker-compose.raspberry.yml`) bind-mounts the PWM chip tree at
  `/pwm` and points `Raspberry:PWM:ChipPath` there. No `privileged` container
  and no host bridge. The device tree routes the channels to the motor enables
  (`dtoverlay=pwm-2chan,pin=12,func=4,pin2=13,func2=4`, Alt0).
- **Consequences:** `docker compose up` on the Pi delivers the full stack —
  frontend, backend (real GPIO + PWM), uStreamer (MJPEG fallback) and go2rtc
  (WebRTC) — using the same images as the PC. Costs: the chip/channel mapping is
  operator configuration (`.env` `PWM_CHIP_PATH` / `PWM_CHANNEL_12/13` plus
  `pwm.env`), and `/pwm` is a container convention chosen to avoid Docker's
  read-only `/sys`.
- **Revisit when:** a future Docker/kernel changes `/sys` mount semantics or a
  cleaner sysfs share appears.
