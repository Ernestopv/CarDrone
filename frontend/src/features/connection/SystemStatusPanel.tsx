import { Panel } from '../../components/ui/Panel'
import { StatusIndicator } from '../../components/ui/StatusIndicator'
import type { SystemServices } from '../../types/drone'
import { serviceView } from '../../utils/status'

interface SystemStatusPanelProps {
  services: SystemServices
}

const SERVICE_ROWS: Array<{ key: keyof SystemServices; label: string }> = [
  { key: 'drone', label: 'Drone' },
  { key: 'raspberryPi', label: 'Raspberry Pi' },
  { key: 'camera', label: 'Camera' },
  { key: 'api', label: 'Backend API' },
]

export function SystemStatusPanel({ services }: SystemStatusPanelProps) {
  return (
    <Panel title="SYSTEM STATUS">
      <ul className="divide-y divide-rule/70">
        {SERVICE_ROWS.map(({ key, label }) => {
          const view = serviceView(services[key])
          return (
            <li
              key={key}
              className="flex items-center justify-between gap-2 py-2.5 first:pt-0 last:pb-0"
            >
              <span className="text-[13px] text-ink-dim">{label}</span>
              <StatusIndicator label={view.label} tone={view.tone} />
            </li>
          )
        })}
      </ul>
    </Panel>
  )
}
