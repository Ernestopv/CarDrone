import { StatusIndicator } from '../../components/ui/StatusIndicator'
import type { DroneStatus } from '../../types/drone'
import { commandLabel, type StatusTone } from '../../utils/status'

interface SystemAlertsProps {
  status: DroneStatus
}

interface Alert {
  id: 'connection' | 'api' | 'camera' | 'command'
  tone: StatusTone
  title: string
  detail: string
}

// Derived exclusively from DroneStatus: an alert exists exactly while the
// failure exists. No local alert state and no dismissal control — a failure
// the operator cannot see is a failure they cannot act on.
function activeAlerts(status: DroneStatus): Alert[] {
  const alerts: Alert[] = []

  if (status.connection === 'error') {
    alerts.push({
      id: 'connection',
      tone: 'danger',
      title: 'CONNECTION LOST',
      detail: 'The drone link dropped. Reconnect to resume control.',
    })
  }

  if (status.services.api === 'error') {
    alerts.push({
      id: 'api',
      tone: 'danger',
      title: 'BACKEND API UNAVAILABLE',
      detail: 'The backend API is not reachable. Status may be stale.',
    })
  }

  // While the link is down the camera alert is redundant: CONNECTION LOST
  // already explains it (and the connection-lost scenario sets both).
  if (status.camera === 'error' && status.connection !== 'error') {
    alerts.push({
      id: 'camera',
      tone: 'warning',
      title: 'CAMERA UNAVAILABLE',
      detail: 'No video signal. The camera panel shows the failure state.',
    })
  }

  if (status.failedCommand !== null) {
    alerts.push({
      id: 'command',
      tone: 'warning',
      title: 'COMMAND FAILED',
      detail: `${commandLabel(status.failedCommand)} was requested but not confirmed. Try again.`,
    })
  }

  return alerts
}

export function SystemAlerts({ status }: SystemAlertsProps) {
  const alerts = activeAlerts(status)
  if (alerts.length === 0) return null

  return (
    <div className="animate-cell-in grid gap-px overflow-hidden rounded-[4px] border border-rule bg-rule">
      {alerts.map((alert) => (
        <div key={alert.id} role="alert" className="bg-panel px-4 py-3">
          <StatusIndicator label={alert.title} tone={alert.tone} />
          <p className="mt-1 pl-4 text-[12px] leading-snug text-ink-dim">{alert.detail}</p>
        </div>
      ))}
    </div>
  )
}
