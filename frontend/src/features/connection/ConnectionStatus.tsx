import { StatusIndicator } from '../../components/ui/StatusIndicator'
import type { ConnectionStatus as ConnectionStatusValue } from '../../types/drone'
import { connectionView } from '../../utils/status'

interface ConnectionStatusProps {
  connection: ConnectionStatusValue
  pending: boolean
  onToggle: () => void
}

const BUTTON_CLASSES =
  'px-3 py-1.5 font-mono text-[11px] font-semibold tracking-[0.14em] transition-colors disabled:cursor-not-allowed disabled:opacity-50'

export function ConnectionStatus({ connection, pending, onToggle }: ConnectionStatusProps) {
  const view = connectionView(connection)
  const connected = connection === 'connected'

  return (
    <div className="flex items-center gap-3">
      <StatusIndicator label={view.label} tone={view.tone} live />
      <button
        type="button"
        onClick={onToggle}
        disabled={pending}
        className={
          connected
            ? `${BUTTON_CLASSES} border border-rule text-ink-dim hover:border-ink-dim`
            : `${BUTTON_CLASSES} border border-hud/50 bg-hud/10 text-hud hover:bg-hud/20`
        }
      >
        {connected ? 'DISCONNECT' : 'CONNECT'}
      </button>
    </div>
  )
}
