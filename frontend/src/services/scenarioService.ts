import type { DroneStatus } from '../types/drone'

/**
 * Phase 1 mock scenarios (specs/frontend/error-states.md).
 *
 * These are explicit test triggers for the four UI failure states. A real
 * backend observes failures — it cannot be asked to simulate them — so this
 * contract deliberately lives outside `DroneService`.
 */
export type MockScenario =
  | 'connectionLost'
  | 'apiUnavailable'
  | 'cameraFailure'
  | 'commandFailure'
  | 'reset'

export interface ScenarioService {
  /**
   * Apply a mock scenario and return the resulting status.
   * Backed by the same mock state as `DroneService`, so commands and
   * scenarios can never drift apart.
   */
  runScenario(scenario: MockScenario): Promise<DroneStatus>
}
