import { Panel } from '../../components/ui/Panel'
import { StatusIndicator } from '../../components/ui/StatusIndicator'
import type { BatteryState, BatteryStatus } from '../../types/drone'
import {
  batteryDirectionView,
  batteryStateView,
  formatCurrent,
  formatPower,
  formatVoltage,
} from '../../utils/status'

interface BatteryPanelProps {
  status: BatteryStatus
}

const BAR_TONES: Record<BatteryState, string> = {
  ok: 'bg-ok',
  low: 'bg-caution',
  critical: 'bg-warn',
  error: 'bg-warn',
  unknown: 'bg-ink-mute',
}

export function BatteryPanel({ status }: BatteryPanelProps) {
  const view = batteryStateView(status.state)
  const percent = status.percent
  const direction = batteryDirectionView(status.current)

  return (
    <Panel title="BATTERY">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[13px] text-ink-dim">State</span>
        <StatusIndicator
          label={view.label}
          tone={view.tone}
          live={status.state === 'error'}
        />
      </div>

      {status.available ? (
        <>
          <div className="mt-3 flex items-center justify-between gap-2">
            <span className="text-[13px] text-ink-dim">Voltage</span>
            <span className="font-mono text-sm font-semibold text-ink">
              {formatVoltage(status.voltage)}
            </span>
          </div>
          <div className="mt-3">
            <div className="flex items-center justify-between gap-2">
              <span className="text-[13px] text-ink-dim">Charge</span>
              <span className="font-mono text-sm font-semibold text-hud">
                {percent === null ? '--' : `${percent}%`}
              </span>
            </div>
            <div
              role="meter"
              aria-label="Battery charge"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={percent ?? 0}
              className="mt-2 h-2 w-full overflow-hidden rounded-sm bg-rule"
            >
              <div
                aria-hidden="true"
                className={`h-full ${BAR_TONES[status.state]}`}
                style={{ width: `${percent ?? 0}%` }}
              />
            </div>
          </div>
          <div className="mt-3 flex items-center justify-between gap-2">
            <span className="text-[13px] text-ink-dim">Current</span>
            <span className="flex items-center gap-2">
              <span className="font-mono text-sm font-semibold text-ink">
                {formatCurrent(status.current)}
              </span>
              {direction && <StatusIndicator label={direction.label} tone={direction.tone} />}
            </span>
          </div>
          <div className="mt-3 flex items-center justify-between gap-2">
            <span className="text-[13px] text-ink-dim">Power</span>
            <span className="font-mono text-sm font-semibold text-ink">
              {formatPower(status.power)}
            </span>
          </div>
        </>
      ) : (
        <p className="mt-3 text-[13px] text-ink-dim">
          {status.state === 'error' ? 'Sensor unavailable' : 'No reading'}
        </p>
      )}

      {/* Honesty marker: the simulated reading is never presented as real. */}
      {status.simulated && (
        <p className="mt-3 font-mono text-[11px] tracking-[0.14em] text-caution">SIMULATED</p>
      )}
    </Panel>
  )
}