import { useEffect, useState } from 'react'
import { AppShell } from './components/layout/AppShell'
import { CameraView } from './features/camera/CameraView'
import { MockScenariosPanel } from './features/connection/MockScenariosPanel'
import { SystemAlerts } from './features/connection/SystemAlerts'
import { SystemStatusPanel } from './features/connection/SystemStatusPanel'
import { DroneControls } from './features/drone-control/DroneControls'
import { useDroneKeyboardControls } from './features/drone-control/useDroneKeyboardControls'
import { TelemetryPanel } from './features/telemetry/TelemetryPanel'
import { useDroneDashboard } from './hooks/useDroneDashboard'
import { droneService, supportsMockScenarios } from './services/droneService'
import type { CameraMode } from './types/drone'

export function App() {
  const { status, connectionPending, toggleConnection, sendCommand, changeSpeed, runScenario } =
    useDroneDashboard()
  // Camera mode is a runtime, same-origin capability (Task 33;
  // specs/integration/camera-stream.md). Resolved once at startup; a failed
  // resolution surfaces as camera-unavailable, never as a simulated feed.
  const [cameraMode, setCameraMode] = useState<CameraMode | null>(null)
  const [cameraModeFailed, setCameraModeFailed] = useState(false)
  useEffect(() => {
    let active = true
    droneService
      .getCameraMode()
      .then((mode) => {
        if (active) setCameraMode(mode)
      })
      .catch(() => {
        if (active) setCameraModeFailed(true)
      })
    return () => {
      active = false
    }
  }, [])
  // Keyboard input is only another way into the very same command handler the
  // visual buttons call; no keyboard-specific state exists anywhere.
  useDroneKeyboardControls({
    requestedCommand: status.requestedCommand,
    onCommand: sendCommand,
  })
  const connected = status.connection === 'connected'

  return (
    <AppShell
      connection={status.connection}
      connectionPending={connectionPending}
      onToggleConnection={toggleConnection}
    >
      <div className="flex flex-col gap-3">
        <SystemAlerts status={status} />
        <div className="animate-cell-in grid gap-px overflow-hidden rounded-[4px] border border-rule bg-rule lg:grid-cols-[minmax(0,1fr)_360px] xl:grid-cols-[minmax(0,1fr)_400px]">
          <div className="flex min-w-0 flex-col gap-px bg-rule">
            <CameraView
              status={status.camera}
              confirmedCommand={status.confirmedCommand}
              speed={status.speed}
              cameraMode={cameraMode}
              cameraUnavailable={cameraModeFailed}
            />
            <DroneControls
              connected={connected}
              requestedCommand={status.requestedCommand}
              speed={status.speed}
              onCommand={sendCommand}
              onSpeedChange={changeSpeed}
            />
          </div>
          <div className="grid gap-px bg-rule sm:grid-cols-2 lg:grid-cols-1">
            <SystemStatusPanel services={status.services} />
            <TelemetryPanel status={status} />
            {/* Mock failure-injection triggers only exist when the simulator
                drives the UI (specs/integration/frontend-backend.md). */}
            {supportsMockScenarios && (
              <MockScenariosPanel disabled={connectionPending} onRunScenario={runScenario} />
            )}
          </div>
        </div>
      </div>
    </AppShell>
  )
}
