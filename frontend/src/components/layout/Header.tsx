import { ConnectionStatus } from '../../features/connection/ConnectionStatus'
import type { ConnectionStatus as ConnectionStatusValue } from '../../types/drone'

interface HeaderProps {
  connection: ConnectionStatusValue
  pending: boolean
  onToggleConnection: () => void
}

export function Header({ connection, pending, onToggleConnection }: HeaderProps) {
  return (
    <header className="border-b border-rule bg-night">
      <div className="mx-auto flex w-full max-w-[1500px] flex-wrap items-center justify-between gap-3 px-4 py-3 lg:px-6">
        <div className="flex items-center gap-3">
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.4"
            strokeLinecap="round"
            className="h-[22px] w-[22px] shrink-0 text-hud"
          >
            <circle cx="5.5" cy="5.5" r="3" />
            <circle cx="18.5" cy="5.5" r="3" />
            <circle cx="5.5" cy="18.5" r="3" />
            <circle cx="18.5" cy="18.5" r="3" />
            <path d="M7.6 7.6 10 10M16.4 7.6 14 10M7.6 16.4 10 14M16.4 16.4 14 14" />
            <rect x="10" y="10" width="4" height="4" rx="1" />
          </svg>
          <h1 className="text-[15px] font-medium tracking-[0.22em] text-ink">
            DRONE CONTROL
          </h1>
          <span className="border border-caution/40 bg-caution/10 px-1.5 py-0.5 font-mono text-[10px] font-semibold tracking-[0.14em] text-caution">
            SIMULATED
          </span>
        </div>
        <ConnectionStatus
          connection={connection}
          pending={pending}
          onToggle={onToggleConnection}
        />
      </div>
    </header>
  )
}
