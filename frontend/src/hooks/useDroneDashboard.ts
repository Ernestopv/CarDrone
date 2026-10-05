import { useEffect, useState } from 'react'
import {
  droneService,
  scenarioService,
  supportsMockScenarios,
} from '../services/droneService'
import type { MockScenario } from '../services/scenarioService'
import type { DroneCommand, DroneStatus } from '../types/drone'
import {
  clampSpeed,
  createConnectingDroneStatus,
  createErrorDroneStatus,
  createInitialDroneStatus,
  normalizeDroneStatus,
} from '../utils/status'

export interface UseDroneDashboardResult {
  status: DroneStatus
  connectionPending: boolean
  toggleConnection: () => Promise<void>
  sendCommand: (command: DroneCommand) => Promise<void>
  changeSpeed: (speed: number) => void
  /** Phase 1 only: apply a mock failure scenario and publish its status. */
  runScenario: (scenario: MockScenario) => Promise<void>
}

// Dashboard state orchestration: optimistic requests, acknowledgement-gated
// confirmation, and error routing. State arrives through the DroneService
// abstraction (API-backed by default since Task 19); this hook never performs
// network work itself and presentation components only receive explicit props.
export function useDroneDashboard(): UseDroneDashboardResult {
  // Lazy initializer: React calls it on mount and no object is shared.
  const [status, setStatus] = useState<DroneStatus>(createInitialDroneStatus)
  const [connectionPending, setConnectionPending] = useState(false)

  // Never swallow a service error: log the cause, then surface error state.
  // The camera moves to "error" with the drone so the UI never shows a
  // stream after a failed service call.
  const reportServiceFailure = (error: unknown) => {
    console.error('Drone service call failed:', error)
    setStatus((current) => createErrorDroneStatus(current))
  }

  // Startup load (specs/integration/frontend-backend.md): the backend's
  // simulator survives browser reloads, so the placeholder offline state is
  // only a guess until the truth arrives. An unreachable backend surfaces as
  // the existing error state instead of pretending the drone is idle.
  useEffect(() => {
    droneService
      .getStatus()
      .then(setStatus)
      .catch(reportServiceFailure)
    // One startup read only: later updates flow through user actions.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const toggleConnection = async () => {
    if (connectionPending) return
    setConnectionPending(true)
    try {
      if (status.connection === 'connected') {
        setStatus(await droneService.disconnect())
        return
      }
      // Reflect the connecting state immediately while the service resolves.
      setStatus((current) => createConnectingDroneStatus(current))
      setStatus(await droneService.connect())
    } catch (error) {
      reportServiceFailure(error)
    } finally {
      setConnectionPending(false)
    }
  }

  const sendCommand = async (command: DroneCommand) => {
    const connected = status.connection === 'connected'
    // STOP always records a request; other commands are ignored when offline.
    if (!connected && command !== 'stop') return
    // The request is optimistic. The confirmed command only moves when the
    // service acknowledges — the UI never sets it directly. A previous
    // failure no longer applies to the command being requested right now.
    setStatus((current) => ({
      ...current,
      requestedCommand: command,
      failedCommand: null,
    }))
    try {
      // The mock only returns a simulated acknowledgement; it never confirms
      // that real hardware executed the command.
      const ack = await droneService.sendCommand(command)
      setStatus((current) =>
        current.connection === 'connected'
          ? { ...current, confirmedCommand: ack.command }
          : current,
      )
    } catch (error) {
      // A rejected command is not a system failure: log it, then reload the
      // service state (the mock records failedCommand there) — never fake a
      // confirmation and never tear the whole drone down for one command.
      console.error('Drone service rejected the command:', error)
      try {
        setStatus(await droneService.getStatus())
      } catch (statusError) {
        // The reload failed too: the service itself is unreachable.
        reportServiceFailure(statusError)
      }
    }
  }

  const changeSpeed = (speed: number) => {
    const nextSpeed = clampSpeed(speed)
    setStatus((current) => ({ ...current, speed: nextSpeed }))
    if (status.connection !== 'connected') return
    droneService.setSpeed(nextSpeed).catch(reportServiceFailure)
  }

  // Mock scenarios are applied by the service and published as ordinary
  // state — the hook owns the state, presentation components only read it.
  // In API mode the panel is not rendered at all; this guard makes any direct
  // call a silent no-op rather than letting a rejection drive the UI to an
  // error state over a dev-tool press.
  const runScenario = async (scenario: MockScenario) => {
    if (!supportsMockScenarios) return
    try {
      setStatus(await scenarioService.runScenario(scenario))
    } catch (error) {
      reportServiceFailure(error)
    }
  }

  // Single point of exit: consumers always receive a state that satisfies the
  // spec's state model, even if a hot-reload preserved an older shape.
  return {
    status: normalizeDroneStatus(status),
    connectionPending,
    toggleConnection,
    sendCommand,
    changeSpeed,
    runScenario,
  }
}
