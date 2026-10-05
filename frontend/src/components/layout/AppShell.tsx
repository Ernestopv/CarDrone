import type { ReactNode } from 'react'
import type { ConnectionStatus } from '../../types/drone'
import { Header } from './Header'

interface AppShellProps {
  connection: ConnectionStatus
  connectionPending: boolean
  onToggleConnection: () => void
  children: ReactNode
}

export function AppShell({
  connection,
  connectionPending,
  onToggleConnection,
  children,
}: AppShellProps) {
  return (
    <div className="min-h-screen bg-night text-ink">
      <Header
        connection={connection}
        pending={connectionPending}
        onToggleConnection={onToggleConnection}
      />
      <main
        aria-label="Drone control dashboard"
        className="mx-auto w-full max-w-[1500px] px-4 py-4 lg:px-6 lg:py-6"
      >
        {children}
      </main>
    </div>
  )
}
