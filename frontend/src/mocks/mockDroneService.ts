import type { DroneService } from '../services/droneService'
import type { MockScenario, ScenarioService } from '../services/scenarioService'
import type { BatteryStatus, CameraMode, CommandAck, DroneCommand, DroneStatus } from '../types/drone'
import {
  clampSpeed,
  createApiUnavailableStatus,
  createCameraFailureStatus,
  createConnectingDroneStatus,
  createErrorDroneStatus,
  createInitialDroneStatus,
  createResetDroneStatus,
  createSystemServices,
} from '../utils/status'

// Simulated drone state for Phase 1.
// Nothing in this file talks to real hardware, networking, GPIO or video.

const CONNECTION_DELAY_MS = 700
const COMMAND_ACK_DELAY_MS = 250

function wait(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function cloneStatus(status: DroneStatus): DroneStatus {
  return { ...status, services: { ...status.services } }
}

export function createMockDroneService(): DroneService & ScenarioService {
  let status = createInitialDroneStatus()
  // Armed by the commandFailure scenario; consumed by the next movement
  // command. STOP never consumes it and never fails.
  let failNextCommand = false

  return {
    getStatus: async () => cloneStatus(status),

    // The simulator is the mock camera deployment: mode is always 'mock'
    // (Task 33; specs/integration/camera-stream.md).
    getCameraMode: async (): Promise<CameraMode> => 'mock',

    // Deterministic simulated battery (Task 41; specs/hardware/battery-monitoring.md).
    // Explicitly labeled simulated so the UI can never present it as a reading
    // from the real sensor.
    getBattery: async (): Promise<BatteryStatus> => ({
      available: true,
      voltage: 7.8,
      percent: 64,
      state: 'ok',
      simulated: true,
      current: -0.45,
      power: -3.51,
    }),

    connect: async () => {
      status = createConnectingDroneStatus(status)
      await wait(CONNECTION_DELAY_MS)
      status = {
        ...status,
        connection: 'connected',
        camera: 'streaming',
        services: createSystemServices('online'),
        requestedCommand: 'stop',
        confirmedCommand: 'stop',
        failedCommand: null,
        speed: 0,
      }
      return cloneStatus(status)
    },

    disconnect: async () => {
      status = createInitialDroneStatus()
      return cloneStatus(status)
    },

    sendCommand: async (command: DroneCommand): Promise<CommandAck> => {
      if (status.connection !== 'connected') {
        // Nothing to command while offline; resolve without confirming anything.
        return { command, source: 'simulated', confirmedByHardware: false }
      }
      // commandFailure scenario: reject the next movement command. The
      // rejection records which command failed and never confirms it; STOP is
      // exempt so the rest state always remains reachable.
      if (failNextCommand && command !== 'stop') {
        failNextCommand = false
        status = { ...status, requestedCommand: command, failedCommand: command }
        throw new Error(`Mock scenario rejected the "${command}" command`)
      }
      status = { ...status, requestedCommand: command, failedCommand: null }
      // Simulated acknowledgement delay — never a hardware confirmation.
      await wait(COMMAND_ACK_DELAY_MS)
      status = { ...status, confirmedCommand: command }
      return { command, source: 'simulated', confirmedByHardware: false }
    },

    setSpeed: async (speed: number) => {
      if (status.connection === 'connected') {
        status = { ...status, speed: clampSpeed(speed) }
      }
      return cloneStatus(status)
    },

    // Phase 1 mock-only failure triggers. They mutate the same internal
    // state the commands use, so scenarios and commands never drift apart.
    runScenario: async (scenario: MockScenario): Promise<DroneStatus> => {
      switch (scenario) {
        case 'connectionLost':
          status = createErrorDroneStatus(status)
          break
        case 'apiUnavailable':
          status = createApiUnavailableStatus(status)
          break
        case 'cameraFailure':
          status = createCameraFailureStatus(status)
          break
        case 'commandFailure':
          // Arming only: the state changes when the next command arrives.
          failNextCommand = true
          break
        case 'reset':
          failNextCommand = false
          status = createResetDroneStatus(status)
          break
      }
      return cloneStatus(status)
    },
  }
}
