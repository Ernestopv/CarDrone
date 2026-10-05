# Backlog - CarDrone

## Project

Drone Control

## Development Strategy

Development will be incremental.

The current priority is to complete the frontend before implementing backend or hardware integration.

Tasks should be completed one at a time.

Do not start the next task automatically.

---

# Phase 1 — Frontend

## Task 1 — Initial Frontend Setup

Status:

```text
COMPLETED
```

Goals:

- Create React + TypeScript + Vite project.
- Configure Tailwind CSS.
- Create initial dashboard.
- Create camera placeholder.
- Create directional controls.
- Create STOP control.
- Create speed control.
- Create system status panel.
- Create telemetry panel.
- Use local/mock state only.

---

## Task 2 — Frontend Structure and UX

Status:

```text
COMPLETED
```

Goals:

- Refactor the initial dashboard.
- Separate major features into components.
- Simplify `App.tsx`.
- Create reusable status components.
- Centralize drone domain types.
- Improve responsive layout.
- Remove the emergency stop button from the UI.
- Improve accessibility.
- Preserve existing behavior.

Expected components include:

```text
CameraView
DroneControls
DirectionButton
SpeedControl
SystemStatusPanel
StatusIndicator
TelemetryPanel
```

No backend or hardware integration.

---

## Task 3 — Drone Simulator

Status:

```text
COMPLETED
```

Goals:

Create a frontend simulation layer that allows the application to behave as if a drone were connected.

Simulate states such as:

```text
Disconnected
Connecting
Connected
Error
```

Simulate:

- Drone connection.
- Raspberry Pi connection.
- API connection.
- Camera state.
- Drone commands.
- Speed.
- Command acknowledgement.

Create a mock service abstraction.

Example concept:

```text
DroneService
    ↓
MockDroneService
```

The frontend should no longer depend directly on hardcoded state values.

No network communication.

---

## Task 4 — Camera Simulation

Status:

```text
COMPLETED
```

Goals:

Improve `CameraView`.

Support:

```text
offline
connecting
streaming
error
```

Provide a simulated video state.

Do not integrate uStreamer yet.

Prepare the component so a real stream can later replace the mock source.

---

## Task 5 — Keyboard Controls

Status:

```text
COMPLETED
```

Goals:

Add keyboard controls.

Proposed mapping:

```text
W       Forward
S       Backward
A       Left
D       Right
Space   Stop
```

Requirements:

- Prevent accidental repeated commands where inappropriate.
- Ignore keyboard controls when typing inside form fields.
- Keep visual controls synchronized with keyboard input.

No physical drone control.

---

## Task 6 — Frontend Error States

Status:

```text
COMPLETED
```

Goals:

Design and implement UI states for:

- API unavailable.
- Camera unavailable.
- Command failure.
- Connection lost.

Use mock scenarios only.

---

## Task 7 — Frontend Final Review

Status:

```text
COMPLETED
```

Goals:

Complete

# Phase 2 — Backend Foundation

## Task 8 — .NET Solution Setup

Status:

```text
COMPLETED
```

Goals:

Create the initial backend inside:

```text
backend/
```

Expected structure:

```text
backend/
├── DroneControl.sln
└── src/
    ├── DroneControl.Api/
    ├── DroneControl.Application/
    ├── DroneControl.Domain/
    └── DroneControl.Infrastructure/
```

Requirements:

- Use ASP.NET Core.
- Configure project references.
- Keep Domain independent.
- Configure dependency injection.
- Add standard logging.
- Add development configuration.
- Configure development CORS.
- Enable OpenAPI in development.
- Add `/api/health`.
- Ensure backend runs on Windows and Linux.

Out of scope:

- Docker.
- Raspberry Pi.
- GPIO.
- PWM.
- SignalR.
- Real drone commands.

Specification:

```text
specs/backend/backend-setup.md
```

---

## Task 9 — Backend Domain Model

Status:

```text
COMPLETED
```

Goals:

Define backend domain concepts.

Create types for:

```text
DroneCommand
DroneStatus
ConnectionStatus
CameraStatus
DroneState
```

Supported commands:

```text
forward
backward
left
right
stop
```

Requirements:

- Use strong C# types.
- Validate speed range `0-100`.
- Keep Domain independent of ASP.NET Core.
- Keep Domain independent of GPIO libraries.
- Do not expose pin numbers to upper layers.

Specification:

```text
specs/backend/domain-model.md
```

---

## Task 10 — Drone Application Service

Status:

```text
COMPLETED
```

Goals:

Create the application-level service abstraction.

Expected capability:

```text
GetState
Connect
Disconnect
SendCommand
SetSpeed
```

Suggested abstraction:

```text
IDroneService
```

Architecture:

```text
API
 ↓
Application
 ↓
Drone abstraction
```

Requirements:

- Controllers must not own drone logic.
- Application layer must not access GPIO directly.
- Use dependency injection.
- Keep services testable.

Specification:

```text
specs/backend/drone-application-service.md
```

---

## Task 11 — Backend Drone Simulator

Status:

```text
COMPLETED
```

Goals:

Create a simulated backend drone implementation.

Concept:

```text
IDroneController
        ↓
MockDroneController
```

Simulate:

- connect;
- disconnect;
- requested command;
- confirmed command;
- speed;
- camera state;
- Raspberry Pi state.

No real hardware communication.

Specification:

```text
specs/backend/drone-simulator.md
```

---

## Task 12 — Drone REST API

Status:

```text
COMPLETED
```

Goals:

Expose backend drone functionality through ASP.NET Core.

Initial endpoints:

```text
GET  /api/drone/status

POST /api/drone/connect
POST /api/drone/disconnect

POST /api/drone/command

PUT  /api/drone/speed
```

Requirements:

- Validate inputs.
- Return correct HTTP status codes.
- Keep controllers thin.
- Do not expose GPIO information.
- Use application services.

Specification:

```text
specs/backend/drone-api.md
```

---

# Phase 3 — Containerization

## Task 13 — Dockerize Backend

Status:

```text
COMPLETED
```

Goals:

Create a Docker image for the ASP.NET Core backend.

Expected files:

```text
backend/
├── Dockerfile
├── .dockerignore
└── ...
```

Requirements:

- Use a multi-stage Docker build.
- Use official .NET images.
- Build the backend inside the container.
- Run only the required runtime in the final image.
- Expose the backend HTTP port.
- Support configuration through environment variables.
- Keep the image independent from Raspberry Pi hardware.

Validation:

```text
docker build
docker run
```

Verify:

```text
GET /api/health
```

from the running container.

Specification:

```text
specs/docker/backend-container.md
```

---

## Task 14 — Docker Compose Development Environment

Status:

```text
COMPLETED
```

Goals:

Create a local container-based development environment.

Expected root file:

```text
docker-compose.yml
```

Initial architecture:

```text
Docker Compose
└── backend
```

The frontend may initially continue to run outside Docker.

Requirements:

- Start backend using `docker compose up`.
- Map backend port to the host.
- Configure development environment variables.
- Add health check where useful.
- Keep secrets out of source control.

Specification:

```text
specs/docker/docker-compose-development.md
```

---

## Task 15 — Multi-Architecture Docker Build

Status:

```text
COMPLETED
```

Goals:

Ensure backend container can target:

```text
linux/amd64
linux/arm64
```

This prepares deployment to Raspberry Pi ARM64.

Requirements:

- Validate Docker Buildx workflow.
- Keep Dockerfile architecture-neutral.
- Do not introduce x86-only dependencies.
- Document build commands.

Example target:

```text
linux/amd64
linux/arm64
```

Specification:

```text
specs/docker/multi-arch.md
```

---

## Task 16 — Backend Error Handling

Status:

```text
COMPLETED
```

Goals:

Implement consistent error handling.

Handle:

- invalid command;
- invalid speed;
- command while disconnected;
- unavailable drone implementation;
- unexpected server errors.

Requirements:

- Return consistent API errors.
- Log unexpected failures.
- Avoid stack traces in production responses.

Specification:

```text
specs/backend/error-handling.md
```

---

## Task 17 — Backend Tests

Status:

```text
COMPLETED
```

Goals:

Create automated backend tests.

Suggested projects:

```text
backend/tests/
├── DroneControl.Domain.Tests/
├── DroneControl.Application.Tests/
└── DroneControl.Api.Tests/
```

Test:

- domain validation;
- command handling;
- speed validation;
- connection rules;
- simulator behavior;
- API endpoints.

No hardware tests yet.

Specification:

```text
specs/backend/backend-tests.md
```

---

# Phase 4 — Frontend / Backend Integration

## Task 18 — Frontend API Drone Service

Status:

```text
COMPLETED
```

Goals:

Create a real frontend API implementation.

Current:

```text
DroneService
    ↓
MockDroneService
```

Target:

```text
DroneService
    ↓
ApiDroneService
    ↓
ASP.NET Core API
```

Requirements:

- Preserve existing service abstraction.
- Do not call `fetch` directly from UI components.
- Configure backend URL through environment variables.
- Keep mock service available when useful.

Specification:

```text
specs/integration/frontend-api-service.md
```

---

## Task 19 — Connect Frontend to Backend

Status:

```text
COMPLETED
```

Goals:

Replace simulated frontend commands with real calls to the backend.

Integrate:

- status;
- connect;
- disconnect;
- command;
- speed.

Architecture:

```text
React
 ↓
ApiDroneService
 ↓
ASP.NET Core
 ↓
MockDroneController
```

No Raspberry Pi required yet.

Requirements:

- Preserve UI behavior.
- Keep command acknowledgement semantics.
- Handle backend failures correctly.
- Do not introduce hardware dependencies.

Specification:

```text
specs/integration/frontend-backend.md
```

---

## Task 20 — Full Stack Docker Compose

Status:

```text
COMPLETED
```

Goals:

Create a reproducible full-stack development environment.

Target architecture:

```text
Docker Compose
├── frontend
└── backend
```

Requirements:

- Containerize frontend if not already done.
- Configure frontend-to-backend networking.
- Avoid hardcoded localhost assumptions inside containers.
- Support development environment variables.
- Add service dependency/health behavior where useful.
- Run the full application with one command.

Expected command:

```text
docker compose up
```

Validation:

- Frontend loads.
- Backend health endpoint responds.
- Frontend communicates with backend.
- Drone simulator works end-to-end.

Specification:

```text
specs/docker/full-stack-compose.md
```

# Phase 5 — Unified Runtime Architecture

## Task 21 — Unified Runtime Architecture

Status:

```text
COMPLETED
```

Goals:

Define the final runtime architecture for CarDrone.

The same project folder must be able to run on:

```text
Windows/Linux development PC
Raspberry Pi ARM64
```

using:

```bash
docker compose up
```

Development PC mode:

```text
Frontend         REAL
Backend          REAL
API              REAL
Hardware         MOCK
Camera           MOCK
```

Raspberry Pi mode:

```text
Frontend         REAL
Backend          REAL
API              REAL
Hardware         REAL
Camera           REAL
```

Requirements:

- Use the same source code on PC and Raspberry Pi.
- Do not require source-code changes between platforms.
- Platform differences must come from configuration/environment.
- Define runtime configuration for hardware.
- Define runtime configuration for camera.
- Keep frontend independent from GPIO implementation.
- Keep hardware-specific code isolated.
- Keep camera transport separate from drone-control transport.
- Preserve current frontend/backend API communication.
- Avoid introducing a separate remote Raspberry Pi service unless technically required.

Expected configuration concept:

```text
HARDWARE_MODE=mock | dry-run | real
CAMERA_MODE=mock | ustreamer
```

Target architecture:

```text
Browser
   ↓
Frontend nginx
   ├── /api/     → ASP.NET Core
   └── /camera/  → uStreamer when enabled
```

Backend hardware architecture:

```text
ASP.NET Core
    ↓
Hardware abstraction
    ├── Mock hardware
    └── Raspberry Pi hardware
```

Specification:

```text
specs/architecture/runtime-deployment.md
```

---

## Task 22 — Hardware Abstraction

Status:

```text
COMPLETED
```

Goals:

Create the application-level hardware abstraction used by the backend.

Suggested abstraction:

```text
IDroneHardware
```

Expected implementations:

```text
IDroneHardware
      ↑
      ├── MockDroneHardware
      └── RaspberryDroneHardware
```

Responsibilities:

- receive semantic drone commands;
- apply requested speed;
- expose hardware availability/status;
- report command execution result;
- hide GPIO/PWM implementation details from upper layers.

Requirements:

- Application/API code must not access GPIO directly.
- Mock implementation must work on Windows/Linux without Raspberry Pi hardware.
- Raspberry implementation must support Linux ARM64.
- Preserve existing domain commands.
- Do not implement real GPIO behavior yet.
- Do not expose physical pin numbers to React.

Specification:

```text
specs/hardware/hardware-abstraction.md
```

---

## Task 23 — Raspberry Pi Hardware Provider

Status:

```text
COMPLETED
```

Goals:

Create the Raspberry Pi-specific hardware provider behind `IDroneHardware`.

Expected architecture:

```text
ASP.NET Core
    ↓
IDroneHardware
    ↓
RaspberryDroneHardware
    ↓
GPIO / PWM abstractions
```

Requirements:

- Support `linux/arm64`.
- Keep Raspberry Pi dependencies isolated.
- Do not place GPIO code in controllers.
- Do not implement unverified motor mappings.
- Support configuration-based runtime selection.
- Fail clearly if `real` mode is selected on an unsupported platform.
- Keep implementation testable through abstractions.

Specification:

```text
specs/hardware/raspberry-hardware-provider.md
```

---

## Task 24 — Raspberry Pi Docker Hardware Access

Status:

```text
COMPLETED
```

Goals:

Define and implement the Docker runtime configuration boundary required for Raspberry Pi hardware access.
This task prepares the application so Raspberry Pi device mappings and permissions can be supplied later through configuration without changing application source code.
Target-specific Raspberry Pi device paths, group IDs, ownership, and permissions may remain unverified until the Raspberry Pi runtime validation tasks.

Requirements:

- Define a configuration-driven mechanism for Raspberry Pi hardware access.
- Keep PC mock mode free of Raspberry Pi device requirements.
- Support linux/arm64.
- Avoid privileged: true by default.
- Prefer least-privilege device and permission configuration.
- Do not invent Raspberry Pi device paths.
- Do not hardcode unverified device paths, group IDs, or permissions.
- Preserve the existing non-root backend runtime by default.
- PC mode must continue to work without Raspberry Pi devices.
- Switching between PC and Raspberry Pi mode must not require application source-code changes.
- Document how target Raspberry Pi device paths, ownership, groups, and permissions will be discovered later.
- Real Raspberry Pi device and permission validation is deferred to later Raspberry Pi runtime/end-to-end tasks.

Validation:

PC docker compose up
→ works without Raspberry hardware
Expected validation:
PC Compose: PASS
Mock hardware mode: PASS
linux/arm64 image build: PASS
No hardcoded Pi device paths: PASS
privileged mode disabled by default: PASS
Actual Raspberry Pi device paths: NOT VERIFIED
Actual Raspberry Pi permissions: NOT VERIFIED
Raspberry Pi runtime hardware access: NOT VERIFIED
Deferred Raspberry Pi validation does not block completion of this task.
Use BLOCKED only if the configuration mechanism itself cannot be implemented without target-specific information.

Specification:

```text
specs/docker/raspberry-hardware-access.md
```

---

# Phase 6 — GPIO and Motor Control

## Task 25 — GPIO Abstraction

Status:

```text
COMPLETED
```

The concrete Linux GPIO API and physical validation remain deferred per
`specs/hardware/gpio.md`; Task 25 implements the testable adapter boundary.

Note:

Task 24 defines the Docker configuration boundary for Raspberry Pi hardware
access. Target-specific GPIO device nodes, ownership, groups, and permissions
may remain NOT VERIFIED during this task.

The GPIO abstraction and Raspberry adapter must be implemented without
guessing target-specific Linux device paths or permissions.

Real Raspberry Pi GPIO runtime validation is deferred to later Raspberry Pi
runtime/end-to-end validation tasks.

Known GPIO configuration (single source of truth):
[`docs/hardware/WIRING.md`](../../docs/hardware/WIRING.md), section
"Given configuration". Values match `backend/src/DroneControl.Api/appsettings.json`
and `docs/PRD.md`.

Goals:

Create a GPIO abstraction.

Suggested architecture:

```text
IGpioController
       ↑
       ├── MockGpioController
       └── RaspberryGpioController
```

Requirements:

- GPIO numbers must come from configuration.
- Do not hardcode pins throughout application code.
- Keep GPIO access isolated from business logic.
- Keep implementation replaceable/mockable.
- PC development must not require Raspberry Pi hardware.
- Raspberry implementation must support ARM64 Linux.
- Do not define motor direction yet.

Specification:

```text
specs/hardware/gpio.md
```

---

## Task 26 — Hardware Fail-Safe

Status:

```text
COMPLETED
```

Goals:

Define safe behavior before enabling physical motor control.

Failure scenarios:

- application startup;
- application shutdown;
- backend failure;
- command timeout;
- invalid command;
- hardware exception;
- process crash;
- Raspberry Pi restart;
- Docker container restart.

Expected safe default:

```text
STOP
```

Requirements:

- Define command timeout behavior.
- Define safe startup state.
- Define safe shutdown state.
- Define recovery behavior.
- A previous movement command must never remain active indefinitely.
- Safety logic must not depend on frontend behavior.
- Do not claim physical STOP behavior until tested.

Specification:

```text
specs/hardware/failsafe.md
```

---

## Task 27 — Hardware Dry-Run Mode

Status:

```text
COMPLETED
```

Goals:

Support safe testing of the real Raspberry Pi software stack without activating motors.

Runtime modes:

```text
mock
dry-run
real
```

Behavior:

```text
mock
→ simulated hardware

dry-run
→ Raspberry Pi hardware path selected
→ intended GPIO/PWM operations are logged
→ motors are not energized

real
→ physical GPIO/PWM operations are applied
```

Requirements:

- Use the same command flow for dry-run and real mode.
- Do not create separate business logic for dry-run.
- Configuration selects the mode.
- Log intended motor/GPIO/PWM operations.
- Allow end-to-end command testing on Raspberry Pi before activating motors.

Specification:

```text
specs/hardware/dry-run.md
```

---

## Task 28 — L298N Motor Wiring Documentation

Status:

```text
COMPLETED
```

Goals:

Document the actual physical wiring between Raspberry Pi, L298N and motors.

Motor driver:

```text
L298N
```

Known Raspberry Pi GPIO configuration:

```text
Pin1 = GPIO23
Pin2 = GPIO24
Pin3 = GPIO21
Pin4 = GPIO20
PWM1 = GPIO12
PWM2 = GPIO13
```

Create:

```text
docs/hardware/WIRING.md
```

Document:

- Raspberry GPIO → L298N input mapping;
- `IN1`;
- `IN2`;
- `IN3`;
- `IN4`;
- `ENA`;
- `ENB`;
- Motor A;
- Motor B;
- motor terminal orientation;
- power wiring assumptions;
- ground/common-ground requirements;
- chosen STOP electrical state.

Requirements:

- Do not infer wiring from GPIO numbers.
- Use the actual physical connections.
- Do not define forward/backward until motor direction is observed.
- Do not guess electrical limits.

Specification:

```text
specs/hardware/motor-wiring.md
```

---

## Task 29 — Motor Direction Mapping

Status:

```text
COMPLETED
```

Goals:

Map semantic drone commands to verified L298N motor behavior.

Commands:

```text
forward
backward
left
right
stop
```

Expected flow:

```text
DroneCommand
    ↓
MotorController
    ↓
IGpioController
    ↓
L298N
    ↓
Motors
```

Requirements:

- Use confirmed `docs/hardware/WIRING.md`.
- Define HIGH/LOW combinations explicitly.
- Verify actual physical direction before finalizing mappings.
- STOP must have an explicit hardware state.
- Preserve mock mode.
- Preserve dry-run mode.
- Keep motor logic testable without physical hardware.

Specification:

```text
specs/hardware/motor-control.md
```

---

## Task 30 — PWM Speed Control

Status:

```text
COMPLETED
```

Known PWM configuration:

```text
PWM1 = GPIO12
PWM2 = GPIO13
```

Goals:

Map application speed:

```text
0 - 100
```

to PWM output for the L298N.

Expected conceptual flow:

```text
Requested Speed
      ↓
PWM abstraction
      ↓
GPIO12 / GPIO13
      ↓
ENA / ENB
```

Track:

```text
requestedSpeed
appliedSpeed
```

Define:

- PWM frequency;
- duty-cycle mapping;
- minimum usable duty cycle if required;
- maximum duty cycle;
- speed zero behavior;
- shutdown behavior;
- validation.

Requirements:

- Do not guess electrical limits.
- Keep PWM configuration external where appropriate.
- Validate speed values.
- Support mock mode.
- Support dry-run mode.
- Support real mode.

Specification:

```text
specs/hardware/pwm.md
```

---

# Phase 7 — Camera and Video Streaming

## Task 31 — Camera Runtime Design

Status:

```text
COMPLETED
```

Goals:

Define camera behavior for both PC and Raspberry Pi.

PC:

```text
CAMERA_MODE=mock
```

Raspberry Pi:

```text
CAMERA_MODE=ustreamer
```

Preferred architecture:

```text
Browser
   ↓
Frontend nginx
   └── /camera/
          ↓
       uStreamer
          ↓
    Raspberry Pi Camera
```

Define:

- camera mode configuration;
- stream URL strategy;
- stream path;
- port;
- resolution;
- frame rate;
- image quality;
- startup behavior;
- failure behavior;
- reconnect behavior.

Requirements:

- Do not hardcode Raspberry Pi IP addresses into React.
- Keep camera configuration independent from UI components.
- Preserve existing mock camera behavior.
- Keep video transport separate from ASP.NET Core control APIs where possible.

Specification:

```text
specs/hardware/camera-runtime.md
```

---

## Task 32 — uStreamer Container

Status:

```text
COMPLETED
```

Goals:

Run uStreamer as part of the Raspberry Pi Docker Compose environment.

Expected flow:

```text
Raspberry Pi Camera
        ↓
     uStreamer
        ↓
    HTTP/MJPEG
```

Requirements:

- Support Raspberry Pi ARM64.
- Determine actual camera device access requirements.
- Do not invent device paths.
- Configure stream path.
- Configure resolution/FPS/quality.
- Configure startup behavior.
- Configure restart behavior where appropriate.
- Verify uStreamer independently before React integration.
- PC mode must still work without a physical camera.
- Avoid unnecessary privileged container access.

Specification:

```text
specs/docker/ustreamer-container.md
```

---

## Task 33 — Frontend Real Camera Integration

Status:

```text
COMPLETED
```

Goals:

Connect the existing `CameraView` to the real uStreamer feed.

Current:

```text
CameraView
    ↓
Mock/Simulated Camera
```

Target:

```text
Raspberry Pi Camera
        ↓
     uStreamer
        ↓
     /camera/
        ↓
     CameraView
```

Preserve camera states:

```text
offline
connecting
streaming
error
```

Requirements:

- Preserve mock camera mode on PC.
- Use real stream in `CAMERA_MODE=ustreamer`.
- Do not hardcode Raspberry Pi hostname/IP.
- Handle stream load failure.
- Handle connection loss.
- Handle reconnect.
- Do not leave mock stream active when real mode is selected.

Specification:

```text
specs/integration/camera-stream.md
```

---

# Phase 8 — Unified Docker Deployment

## Task 34 — Unified Docker Compose

Status:

```text
COMPLETED
```

Goals:

Produce the final Docker Compose architecture for CarDrone.

Target:

```text
Docker Compose
├── frontend
├── backend
└── camera
```

The backend contains the selected hardware provider.

PC mode:

```text
HARDWARE_MODE=mock
CAMERA_MODE=mock
```

Raspberry Pi mode:

```text
HARDWARE_MODE=real
CAMERA_MODE=ustreamer
```

Requirements:

- Same project structure on PC and Raspberry Pi.
- Same primary `docker compose up` command.
- No source-code changes per platform.
- Runtime differences come from configuration.
- Frontend nginx proxies `/api/`.
- Frontend nginx proxies `/camera/` when camera service is enabled.
- Health checks where useful.
- Dependency ordering where useful.
- ARM64-compatible services.
- PC mode must not require Raspberry hardware.

Specification:

```text
specs/docker/unified-compose.md
```

---

## Task 35 — PC Runtime Mode

Status:

```text
COMPLETED
```

Goals:

Validate the final application on a Windows/Linux development PC.

Procedure:

```text
Copy CarDrone project folder
↓
configure PC mode
↓
docker compose up
```

Expected:

```text
Frontend         REAL
Backend          REAL
API              REAL
Controls         REAL against backend
Hardware         MOCK
Camera           MOCK
```

Requirements:

- No Raspberry Pi required.
- No GPIO required.
- No physical camera required.
- User can connect/disconnect.
- User can send directional commands.
- User can change speed.
- API requests are real.
- Mock hardware receives commands.
- Camera UI works in simulated mode.

Specification:

```text
specs/deployment/pc-mode.md
```

---

## Task 36 — Raspberry Pi Runtime Mode

Status:

```text
COMPLETED
```

Goals:

Run the same project folder on Raspberry Pi ARM64.

Procedure:

```text
Copy CarDrone project folder
↓
configure Raspberry Pi mode
↓
docker compose up
```

Expected:

```text
Frontend         REAL
Backend          REAL
API              REAL
GPIO/PWM         REAL
L298N            REAL
Motors           REAL
Camera           REAL
uStreamer        REAL
```

Requirements:

- No source-code modifications.
- Only configuration/environment may differ from PC.
- Docker images run on ARM64.
- Backend starts.
- Frontend starts.
- Hardware provider selects real implementation.
- Camera service selects uStreamer.
- Required GPIO/camera permissions are documented.

Specification:

```text
specs/deployment/raspberry-mode.md
```

Result (2026-10-05) — verified on the real target (Raspberry Pi 4 Rev 1.1, Ubuntu 22.04 arm64):

- **Docker Compose Pi mode**: backend + frontend start; the hardware provider selects the real implementation (`HARDWARE_MODE=real`); the camera service selects uStreamer (`CAMERA_MODE=ustreamer`).
- **Camera**: uStreamer MJPEG reaches the browser through nginx `/camera/`; `camera-mode.json` resolves the mode.
- **GPIO**: real direction control over libgpiod — all five commands (`forward`/`backward`/`left`/`right`/`stop`) operator-confirmed; non-root container access verified. Recorded in `docs/hardware/WIRING.md`.
- **GPIO/camera permissions**: documented in `docs/hardware/raspberry-pi-inventory.md`.
- **PWM speed (real)**: delivered by the **native launcher** (`scripts/run-native-pi.sh`) because the container cannot write `/sys` (Docker mounts it read-only) — a runtime/deployment difference, not a source-code change. ENA/ENB are driven by `PWM1`/`PWM2` (BCM 12/13) at 20 kHz; speeds verified. See `docs/hardware/WIRING.md` (PWM speed envelope) and `docs/RUNBOOK-PI.md` (Paso 10).
- Still `NOT VERIFIED` (non-blocking, recorded in `docs/hardware/WIRING.md`): exact PWM duty-0 rest/coast/brake behavior and motor/L298N electrical limits.

Note: the referenced specification `specs/deployment/raspberry-mode.md` was never written; the verification record lives here, in `docs/hardware/WIRING.md`, `docs/RUNBOOK-PI.md`, and `MEMORY.md`.

---

# Phase 9 — End-to-End Validation

## Task 37 — End-to-End Robot Control

Status:

```text
NEXT
```

Goals:

Validate the complete control path on Raspberry Pi.

Flow:

```text
React
 ↓
ApiDroneService
 ↓
ASP.NET Core
 ↓
IDroneHardware
 ↓
RaspberryDroneHardware
 ↓
GPIO / PWM
 ↓
L298N
 ↓
Motors
```

Validate:

```text
connect
disconnect
forward
backward
left
right
stop
speed
```

Requirements:

- Commands from React reach physical hardware.
- Requested command is observable.
- Applied/confirmed command is observable.
- Speed changes affect PWM.
- STOP works correctly.
- Invalid commands do not activate hardware.

Specification:

```text
specs/integration/end-to-end-control.md
```

---

## Task 38 — End-to-End Camera

Status:

```text
PENDING
```

Goals:

Validate the complete real camera path.

Flow:

```text
Raspberry Pi Camera
        ↓
     uStreamer
        ↓
    nginx /camera/
        ↓
     CameraView
```

Validate:

- camera startup;
- uStreamer startup;
- real video render;
- camera unavailable state;
- stream loss;
- reconnect;
- browser refresh;
- container restart.

Specification:

```text
specs/integration/end-to-end-camera.md
```

---

## Task 39 — Failure and Recovery Validation

Status:

```text
PENDING
```

Goals:

Validate safe system behavior under failures.

Test:

- backend restart;
- frontend restart;
- Raspberry Pi reboot;
- Docker restart;
- camera service restart;
- camera disconnect;
- hardware exception;
- command timeout;
- application shutdown.

Requirements:

- Motors enter the defined safe STOP state where applicable.
- Previous movement commands do not remain indefinitely active.
- UI reports backend/camera failures.
- System can recover after services return.
- Recovery must not require source-code changes.

Specification:

```text
specs/integration/failure-recovery.md
```

---

## Task 40 — MVP Final Validation

Status:

```text
PENDING
```

Goals:

Declare the CarDrone MVP complete.
