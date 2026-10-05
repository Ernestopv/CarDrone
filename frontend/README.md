# Drone Control — Frontend

Phase 1 of the Drone Control project: a React + TypeScript + Vite + Tailwind CSS
dashboard backed by simulated (mock) data only.

No backend, Raspberry Pi, GPIO or video streaming is involved in this phase.

## Commands

```bash
npm install     # install dependencies
npm run dev     # start the dev server
npm run build   # type-check and build for production
npm run lint    # run oxlint
```

## Structure

```text
src/
├── components/   # shared layout and UI components
├── features/     # camera, connection, drone-control, telemetry panels
├── mocks/        # simulated drone service (Phase 1 only)
├── services/     # service interface used by the UI
├── types/        # shared domain types
└── utils/        # small status/label helpers
```

Components consume `DroneService` only; the mock implementation can later be
replaced by an API implementation without changing the UI.
