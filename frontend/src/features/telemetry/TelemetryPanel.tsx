import type { ReactNode } from 'react'
import { Panel } from '../../components/ui/Panel'
import { StatusIndicator } from '../../components/ui/StatusIndicator'
import type { DroneStatus } from '../../types/drone'
import { cameraView, commandLabel, connectionView } from '../../utils/status'

interface TelemetryPanelProps {
  status: DroneStatus
}

interface TelemetryRowProps {
  label: string
  children: ReactNode
  /** Announce value changes to screen readers (use for fast-changing values). */
  live?: boolean
}

function TelemetryRow({ label, children, live = false }: TelemetryRowProps) {
  return (
    <div className="flex items-center justify-between gap-2 py-2.5 first:pt-0 last:pb-0">
      <dt className="text-[13px] text-ink-dim">{label}</dt>
      <dd aria-live={live ? 'polite' : undefined}>{children}</dd>
    </div>
  )
}

export function TelemetryPanel({ status }: TelemetryPanelProps) {
  const connection = connectionView(status.connection)
  const camera = cameraView(status.camera)

  return (
    <Panel title="TELEMETRY">
      <dl className="divide-y divide-rule/70">
        <TelemetryRow label="Requested Command" live>
          <span className="font-mono text-sm font-semibold tracking-[0.12em] text-hud">
            {commandLabel(status.requestedCommand)}
            {status.failedCommand === status.requestedCommand && (
              <span className="ml-2 text-warn">FAILED</span>
            )}
          </span>
        </TelemetryRow>
        <TelemetryRow label="Confirmed Command" live>
          <span className="font-mono text-sm font-semibold tracking-[0.12em] text-hud">
            {commandLabel(status.confirmedCommand)}
            {/* Pending only while an acknowledgement is genuinely outstanding —
                a failed command shows FAILED above instead of waiting forever. */}
            {status.requestedCommand !== status.confirmedCommand &&
              status.failedCommand !== status.requestedCommand && (
                <span className="text-caution" aria-hidden="true">
                  …
                </span>
              )}
          </span>
        </TelemetryRow>
        <TelemetryRow label="Speed">
          <span className="font-mono text-sm font-semibold text-ink">{status.speed}%</span>
        </TelemetryRow>
        <TelemetryRow label="Connection">
          <StatusIndicator label={connection.label} tone={connection.tone} />
        </TelemetryRow>
        <TelemetryRow label="Camera">
          <StatusIndicator label={camera.label} tone={camera.tone} />
        </TelemetryRow>
      </dl>
      <p className="mt-4 border-t border-rule pt-3 text-[12px] leading-relaxed text-ink-mute">
        Simulated data — commands never reach hardware.
      </p>
    </Panel>
  )
}
