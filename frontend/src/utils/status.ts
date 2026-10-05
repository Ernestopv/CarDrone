import type {
  CameraStatus,
  ConnectionStatus,
  DroneCommand,
  DroneStatus,
  ServiceStatus,
  SystemServices,
} from '../types/drone'

export type StatusTone = 'muted' | 'warning' | 'success' | 'danger'

export interface StatusView {
  label: string
  tone: StatusTone
}

export function createSystemServices(status: ServiceStatus): SystemServices {
  return {
    drone: status,
    raspberryPi: status,
    camera: status,
    api: status,
  }
}

// Single source of truth for the initial (fully offline) drone status.
// Used by both the dashboard hook and the mock service so the two never drift.
export function createInitialDroneStatus(): DroneStatus {
  return {
    connection: 'offline',
    camera: 'offline',
    services: createSystemServices('offline'),
    requestedCommand: 'stop',
    confirmedCommand: 'stop',
    failedCommand: null,
    speed: 0,
  }
}

// Shared definition of the transitional state while a connection attempt runs,
// so the hook and the mock service never describe "connecting" differently.
export function createConnectingDroneStatus(current: DroneStatus): DroneStatus {
  return {
    ...current,
    connection: 'connecting',
    camera: 'connecting',
    services: createSystemServices('connecting'),
  }
}

// Shared definition of a failed connection attempt: the drone and the camera
// both move to "error" together, so the camera never claims a stream while the
// simulator reports a failure. Also used by the connection-lost scenario.
export function createErrorDroneStatus(current: DroneStatus): DroneStatus {
  return {
    ...current,
    connection: 'error',
    camera: 'error',
    services: { ...current.services, drone: 'error', camera: 'error' },
  }
}

// --- Mock scenario transitions (specs/frontend/error-states.md) ---
// Pure helpers so each failure state has exactly one definition. They are
// applied by the mock service only; presentation components never call them.

// The backend API fails while the drone itself stays reachable.
export function createApiUnavailableStatus(current: DroneStatus): DroneStatus {
  return { ...current, services: { ...current.services, api: 'error' } }
}

// The camera feed dies while the drone keeps flying.
export function createCameraFailureStatus(current: DroneStatus): DroneStatus {
  return {
    ...current,
    camera: 'error',
    services: { ...current.services, camera: 'error' },
  }
}

// Clear scenario damage. Never fabricates a connection: offline stays
// offline, an errored drone returns to connected, speed is preserved.
export function createResetDroneStatus(current: DroneStatus): DroneStatus {
  const connection = current.connection === 'error' ? 'connected' : current.connection
  const serviceState: ServiceStatus =
    connection === 'connected' ? 'online' : connection === 'connecting' ? 'connecting' : 'offline'
  const camera: CameraStatus =
    connection === 'connected' ? 'streaming' : connection === 'connecting' ? 'connecting' : 'offline'

  return {
    ...current,
    connection,
    camera,
    services: createSystemServices(serviceState),
    requestedCommand: 'stop',
    confirmedCommand: 'stop',
    failedCommand: null,
  }
}

export function connectionView(connection: ConnectionStatus): StatusView {
  switch (connection) {
    case 'offline':
      return { label: 'DISCONNECTED', tone: 'muted' }
    case 'connecting':
      return { label: 'CONNECTING', tone: 'warning' }
    case 'connected':
      return { label: 'CONNECTED', tone: 'success' }
    case 'error':
      return { label: 'ERROR', tone: 'danger' }
  }
}

export function serviceView(service: ServiceStatus): StatusView {
  switch (service) {
    case 'offline':
      return { label: 'Offline', tone: 'muted' }
    case 'connecting':
      return { label: 'Connecting', tone: 'warning' }
    case 'online':
      return { label: 'Online', tone: 'success' }
    case 'error':
      return { label: 'Error', tone: 'danger' }
  }
}

export function cameraView(camera: CameraStatus): StatusView {
  switch (camera) {
    case 'offline':
      return { label: 'OFFLINE', tone: 'muted' }
    case 'connecting':
      return { label: 'CONNECTING', tone: 'warning' }
    case 'streaming':
      return { label: 'STREAMING', tone: 'success' }
    case 'error':
      return { label: 'ERROR', tone: 'danger' }
  }
}

export function commandLabel(command: DroneCommand): string {
  return command.toUpperCase()
}

// Enforces the spec's state model at runtime. React state can survive a
// development hot-reload with an older or incomplete shape; normalizing at
// the single point of exit guarantees every consumer sees a valid DroneStatus.
// Missing commands fail safe to "stop" — the drone's rest state.
export function normalizeDroneStatus(current: DroneStatus): DroneStatus {
  return {
    ...current,
    connection: current.connection ?? 'offline',
    camera: current.camera ?? 'offline',
    services: current.services ?? createSystemServices('offline'),
    requestedCommand: current.requestedCommand ?? 'stop',
    confirmedCommand: current.confirmedCommand ?? 'stop',
    failedCommand: current.failedCommand ?? null,
    speed: current.speed ?? 0,
  }
}

export function clampSpeed(speed: number): number {
  return Math.min(100, Math.max(0, Math.round(speed)))
}
