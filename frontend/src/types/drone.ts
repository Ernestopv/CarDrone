export type ConnectionStatus = 'offline' | 'connecting' | 'connected' | 'error'

export type ServiceStatus = 'offline' | 'connecting' | 'online' | 'error'

export type CameraStatus = 'offline' | 'connecting' | 'streaming' | 'error'

/** Battery level state (specs/hardware/battery-monitoring.md). */
export type BatteryState = 'unknown' | 'ok' | 'low' | 'critical' | 'error'

export interface BatteryStatus {
  /** False when no valid reading exists (sensor unreachable or not polled yet). */
  available: boolean
  /** Measured pack/bus voltage in volts; null when unavailable. */
  voltage: number | null
  /** Linear-approximation state of charge 0-100; null when unknown. */
  percent: number | null
  state: BatteryState
  /** True only for the simulated (mock) reading; the UI labels it. */
  simulated: boolean
  /** Signed current in amps (raw shunt polarity); null when unavailable. */
  current: number | null
  /** Signed power in watts (voltage × current); null when unavailable. */
  power: number | null
}

/**
 * Runtime camera deployment mode (Task 33; specs/integration/camera-stream.md).
 * Mirrors the deployment's CAMERA_MODE: resolved by the service layer from the
 * same-origin capability document — never from a build-time env or a wire
 * field.
 */
export type CameraMode = 'mock' | 'ustreamer'

export type DroneCommand = 'forward' | 'backward' | 'left' | 'right' | 'stop'

export interface SystemServices {
  drone: ServiceStatus
  raspberryPi: ServiceStatus
  camera: ServiceStatus
  api: ServiceStatus
}

export interface DroneStatus {
  connection: ConnectionStatus
  camera: CameraStatus
  services: SystemServices
  /** Last command requested by the user (optimistic UI intent). */
  requestedCommand: DroneCommand
  /** Last command acknowledged by the service. The UI never sets this directly. */
  confirmedCommand: DroneCommand
  /** Set when the service rejected a requested command; null = no failure. */
  failedCommand: DroneCommand | null
  /** Speed percentage from 0 to 100. The PWM relationship is defined later. */
  speed: number
}

export interface CommandAck {
  command: DroneCommand
  /** Phase 1 acknowledgements are always "simulated", never hardware. */
  source: 'simulated' | 'hardware'
  /** True only when real hardware confirmed the command. */
  confirmedByHardware: boolean
}
