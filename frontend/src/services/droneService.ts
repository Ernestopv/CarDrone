import { createMockDroneService } from '../mocks/mockDroneService'
import { createApiDroneService } from './apiDroneService'
import type { ScenarioService } from './scenarioService'
import type { CameraMode, CommandAck, DroneCommand, DroneStatus } from '../types/drone'

export interface DroneService {
  getStatus(): Promise<DroneStatus>
  connect(): Promise<DroneStatus>
  disconnect(): Promise<DroneStatus>
  sendCommand(command: DroneCommand): Promise<CommandAck>
  setSpeed(speed: number): Promise<DroneStatus>
  /** Runtime camera mode (mock | ustreamer); never a wire field (Task 33). */
  getCameraMode(): Promise<CameraMode>
}

const mockService = createMockDroneService()

// Selection seam (Task 18 seam, flipped by specs/integration/frontend-backend.md):
// components keep importing only these exports, so no UI branches on how its
// data is produced. Task 19 makes the API-backed implementation the default —
// unset or 'api' talks to VITE_API_BASE_URL (5080 by default); 'mock' (or any
// other value) selects the simulator for backend-less UI development.
const useApiDroneService = (import.meta.env.VITE_DRONE_SERVICE ?? 'api') === 'api'

/**
 * Capability flag for the mock-only failure-injection tooling
 * (specs/frontend/error-states.md). Scenarios mutate simulator state the API
 * cannot observe, so the panel that offers them renders only when the mock is
 * selected. Data/state still flows exclusively through `DroneService`.
 */
export const supportsMockScenarios = !useApiDroneService

export const droneService: DroneService = useApiDroneService
  ? createApiDroneService(import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080')
  : mockService

/**
 * Phase 1 mock-only failure triggers (specs/frontend/error-states.md),
 * wired to the same instance as `droneService` so scenarios and commands
 * share one state. Deliberately separate from `DroneService`: a future API
 * implementation observes failures, it cannot be asked to simulate them.
 * In 'api' mode there is nothing to simulate against, so the guard rejects
 * explicitly instead of mutating orphaned mock state.
 */
export const scenarioService: ScenarioService = useApiDroneService
  ? {
      runScenario: async () => {
        throw new Error('Mock scenarios are unavailable when the drone service is API-backed')
      },
    }
  : mockService
