# AGENTS.md - CarDrone

## Project Name

Drone Control

## Project Overview

This project is a web application for monitoring and controlling a drone.

The system will eventually consist of:

- A React frontend for the drone control interface.
- A .NET backend/API.
- A Raspberry Pi installed on the drone.
- GPIO pins used to interface with the drone hardware.
- A camera connected to the Raspberry Pi.
- uStreamer for real-time camera video streaming.
- Communication between the frontend, backend, Raspberry Pi, and drone hardware.

Development must be incremental.

Do not attempt to implement the entire system at once.

---

# Current Development Phase

Phases advance through `tasks/BACKLOG.md` — the roadmap and status authority.
The current implementation task is the `IN_PROGRESS` (or `NEXT`) entry there;
never work ahead of the `NEXT` task. Durable project state is recorded in
`MEMORY.md`.

Phase 1 (frontend with mock data) is complete. Later phases implement the
backend, hardware abstractions, dry-run/real runtime modes, and camera
streaming according to the backlog. The "For now" restrictions below applied
to the phase that introduced them; the active phase is always the backlog's.

---

# Planned Technology Stack

## Frontend

- React
- TypeScript
- Vite
- CSS / Tailwind CSS
- React Router
- ESLint
- Prettier

Additional libraries should only be introduced when they provide a clear benefit.

Avoid unnecessary dependencies.

## Backend

Implemented from Phase 2 onward:

- .NET
- ASP.NET Core Web API
- C#
- SignalR for real-time communication when appropriate

The backend exists in this repository (see `tasks/BACKLOG.md` for its current
task status).

## Hardware

Planned hardware:

- Raspberry Pi
- Camera
- Drone control hardware

## Video

Video streaming is planned to use:

- uStreamer

The Raspberry Pi will expose a camera stream that will later be consumed by the application.

Do not implement real video streaming during the initial frontend phase.

Use a placeholder or simulated camera component.

---

# Raspberry Pi GPIO Configuration

The planned Raspberry Pi GPIO configuration is:

```json
{
  "GPIO": {
    "Pin1": 23,
    "Pin2": 24,
    "Pin3": 21,
    "Pin4": 20,
    "PWM1": 12,
    "PWM2": 13
  }
}
```

These values are hardware configuration references only.

Do not access, modify, initialize, or simulate electrical GPIO behavior during the frontend phase.

Later backend/hardware integration must keep GPIO configuration separate from business logic.

---

# Development Strategy

The project will be implemented in phases.

## Phase 1 — Frontend

Build the complete control interface with mock data.

Goals:

- Create application layout.
- Create drone control dashboard.
- Create camera/video area.
- Create flight/control interface.
- Display simulated telemetry.
- Display connection status.
- Create responsive UI.
- Establish reusable frontend components.

No real hardware interaction.

## Phase 2 — .NET Backend

After the frontend is stable:

- Create ASP.NET Core API.
- Define frontend/backend contracts.
- Implement application services.
- Implement configuration.
- Add logging.
- Add health checks.
- Add real-time communication where required.

## Phase 3 — Raspberry Pi Integration

After the API architecture is established:

- Connect backend services to Raspberry Pi.
- Add Raspberry Pi communication layer.
- Implement hardware abstractions.
- Implement GPIO integration.
- Validate commands before sending them to hardware.

## Phase 4 — Video Streaming

Integrate:

- Raspberry Pi camera.
- uStreamer.
- Video stream endpoint.
- Frontend video component.
- Connection/reconnection handling.

## Phase 5 — Hardware Testing

Only after previous phases are validated:

- Test GPIO commands.
- Test PWM.
- Test hardware communication.
- Test fail-safe behavior.
- Test connection loss behavior.

Hardware testing must be incremental.

---

# Frontend Architecture

Use a feature-oriented structure.

Recommended structure:

```text
src/
├── app/
│   ├── App.tsx
│   ├── router.tsx
│   └── providers/
│
├── components/
│   ├── ui/
│   └── layout/
│
├── features/
│   ├── drone-control/
│   ├── telemetry/
│   ├── camera/
│   └── connection/
│
├── hooks/
├── services/
├── mocks/
├── types/
├── utils/
├── assets/
└── styles/
```

---

# Frontend Features

The initial dashboard should eventually contain the following areas.

## Drone Status

Display:

- Connection state.
- Drone state.
- Camera state.
- Backend state.
- Raspberry Pi state.

During Phase 1 these values are simulated.

Example:

```text
Drone: Offline / Connected
Camera: Offline / Streaming
Raspberry Pi: Offline / Connected
API: Offline / Connected
```

---

# Camera Panel

Create a main video panel.

During Phase 1:

- Use a placeholder.
- Do not connect to uStreamer.
- Maintain the expected video aspect ratio.
- Show connection/loading/offline states.

Later this component will display the uStreamer feed.

Example component:

```text
CameraView
```

Possible states:

```text
offline
connecting
streaming
error
```

---

# Drone Controls

Create a visual control interface for the drone.

The UI may contain controls representing:

- Forward
- Backward
- Left
- Right
- Stop

Additional controls may be introduced later after the hardware behavior is formally defined.

During Phase 1 these controls must NOT send commands anywhere.

They should update local/mock state only.

---

# Telemetry

Create components capable of displaying simulated telemetry.

Potential telemetry includes:

- Connection status
- Signal status
- Camera status
- Current command
- Speed percentage
- System status

Do not invent hardware sensor information unless it has been explicitly defined.

Mock telemetry should be clearly identified as simulated data in the source code.

---

# Mock Layer

All frontend features that will eventually communicate with the backend should initially use a mock service.

Example:

```text
src/services/droneService.ts
```

The service interface should be designed so that mock implementations can later be replaced by API implementations.

Example conceptual interface:

```ts
interface DroneService {
  getStatus(): Promise<DroneStatus>;
  sendCommand(command: DroneCommand): Promise<void>;
}
```

During Phase 1:

```text
DroneService
    ↓
MockDroneService
```

Later:

```text
DroneService
    ↓
ApiDroneService
    ↓
.NET API
```

Frontend components must not know whether the implementation is mock or real.

---

# Domain Types

Centralize domain types.

Example:

```text
src/types/drone.ts
```

Possible types:

```ts
type DroneConnectionStatus = "offline" | "connecting" | "connected" | "error";

type DroneCommand = "forward" | "backward" | "left" | "right" | "stop";
```

Avoid duplicated string values throughout the application.

---

# Component Design Rules

Components should:

- Have a clear responsibility.
- Remain reasonably small.
- Be reusable where appropriate.
- Keep business logic outside presentation components.
- Receive explicit props.
- Use TypeScript types.
- Avoid unnecessary global state.

Do not build the entire dashboard as one large component.

---

# State Management

Start with:

- React state.
- Context when genuinely useful.

Do not introduce Redux, Zustand, MobX, or another global state library unless the application actually requires it.

Prefer the simplest solution that meets current requirements.

---

# Styling

The UI should feel like a modern drone control dashboard.

Design priorities:

- Dark interface.
- High contrast.
- Clear status indicators.
- Large camera area.
- Easily identifiable controls.
- Desktop-first dashboard.
- Responsive behavior.

Avoid excessive animations.

Controls must remain readable and usable.

---

# Safety UI

Even during the frontend-only phase, design the interface so future safety behavior has a clear place.

Reserve UI concepts for:

- Connection loss.
- Command acknowledgement.
- Hardware unavailable.
- Camera unavailable.

The frontend must never visually report that a physical command succeeded unless the backend/hardware has actually confirmed it.

During mock development, simulated acknowledgements must remain distinguishable in the implementation from real hardware acknowledgements.

---

# Backend Architecture — Future Reference

The backend will be implemented later.

Expected conceptual layers:

```text
API
 ↓
Application
 ↓
Drone Services
 ↓
Hardware Abstraction
 ↓
Raspberry Pi / GPIO
```

Do not allow controllers to manipulate GPIO directly.

GPIO interaction must eventually live behind an abstraction.

Example concept:

```text
IDroneController
IGpioController
IVideoStreamService
```

Exact interfaces will be designed during the backend phase.

Do not create them prematurely unless requested.

---

# Hardware Separation

Hardware-specific values must never be hardcoded throughout the application.

GPIO configuration must come from configuration (see the GPIO block in the
[Raspberry Pi GPIO Configuration](#raspberry-pi-gpio-configuration) section),
never from source-code constants.

Hardware code must be isolated from:

- API controllers.
- Business logic.
- UI logic.

---

# Video Architecture — Future Reference

Expected conceptual flow:

```text
Raspberry Pi Camera
        ↓
     uStreamer
        ↓
 HTTP/MJPEG Stream
        ↓
   React Frontend
```

The exact streaming configuration will be defined later.

For Phase 1 use only a frontend placeholder.

---

# Coding Rules

Always:

- Use TypeScript.
- Prefer strict types.
- Avoid `any`.
- Use descriptive names.
- Keep functions focused.
- Reuse existing components.
- Remove unused imports.
- Keep lint clean.
- Keep code formatted consistently.
- Handle errors explicitly.

Do not introduce abstractions that are not needed yet.

---

# Dependency Rules

Before adding a dependency:

1. Check whether React or the existing stack already solves the problem.
2. Explain why the dependency is necessary.
3. Prefer mature and actively maintained packages.
4. Avoid multiple libraries solving the same problem.

Do not install packages automatically just because they are popular.

---

# AI Agent Workflow

Before modifying the project:

1. Read this `AGENTS.md`.
2. Read `docs/PRD.md` if it exists.
3. Read `docs/ARCHITECTURE.md` if it exists.
4. Read `tasks/CURRENT.md` if it exists.
5. Inspect existing source code.
6. Follow existing conventions.

Before writing code, identify the minimum set of files required for the task.

---

# Implementation Workflow

For every task:

1. Understand the requested feature.
2. Inspect related code.
3. Identify affected files.
4. Implement the smallest clean solution.
5. Run TypeScript checks.
6. Run lint.
7. Run tests when available.
8. Fix introduced errors.
9. Summarize changes.

Do not modify unrelated parts of the project.

---

# Commands

When the frontend project exists, prefer the project's package manager.

Typical commands may include:

```bash
npm install
npm run dev
npm run build
npm run lint
npm test
```

Use the commands actually defined in `package.json`.

Do not assume a script exists without checking first.

---

# Documentation Rules

When architecture or requirements change, update the corresponding documentation.

Expected documentation:

```text
AGENTS.md
README.md

docs/
├── PRD.md
├── ARCHITECTURE.md
├── DATABASE.md
└── DECISIONS.md

tasks/
├── BACKLOG.md
└── CURRENT.md
```

Not every file needs to exist from day one.

Add documentation when it becomes useful.

---

# Important Restrictions

Do NOT:

- implement backend code during the frontend phase;
- manipulate GPIO during the frontend phase;
- connect to physical drone hardware during the frontend phase;
- hardcode hardware behavior into React components;
- implement real PWM control before the hardware layer is designed;
- assume GPIO command semantics that have not been specified;
- invent additional hardware;
- silently change the GPIO configuration;
- introduce unnecessary frameworks;
- refactor unrelated code;
- claim hardware operations succeeded without real confirmation.

---

# Current Priority

The current priority is the active backlog task — the `IN_PROGRESS` (or
`NEXT`) entry in `tasks/BACKLOG.md`. Implement one task at a time; do not
start the next task automatically and do not work ahead of it.

---

# Definition of Done — Phase 1 (historical record)

These were the Phase 1 completion criteria; Phase 1 is complete. Current
completion criteria are the acceptance criteria of the active specification.

- The React application builds successfully.
- TypeScript has no relevant errors.
- Lint passes.
- The dashboard has a coherent layout.
- A camera placeholder exists.
- Drone controls exist visually.
- Controls work against mock/local state only.
- Telemetry is displayed using simulated data.
- Connection states can be represented.
- Components are reusable and organized.
- No real hardware interaction exists.
