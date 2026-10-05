import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { DroneStatus } from '../../types/drone'
import { createInitialDroneStatus } from '../../utils/status'
import { TelemetryPanel } from './TelemetryPanel'

function statusWith(overrides: Partial<DroneStatus>): DroneStatus {
  return { ...createInitialDroneStatus(), ...overrides }
}

describe('TelemetryPanel', () => {
  it('shows the initial commands without any marker', () => {
    render(<TelemetryPanel status={createInitialDroneStatus()} />)
    expect(screen.getAllByText('STOP')).toHaveLength(2)
    expect(screen.queryByText('…')).toBeNull()
    expect(screen.queryByText('FAILED')).toBeNull()
  })

  it('shows the pending marker while an acknowledgement is outstanding', () => {
    render(<TelemetryPanel status={statusWith({ requestedCommand: 'forward' })} />)
    expect(screen.getByText('…')).toBeTruthy()
    expect(screen.queryByText('FAILED')).toBeNull()
  })

  it('shows FAILED instead of waiting forever for a rejected command', () => {
    render(
      <TelemetryPanel
        status={statusWith({ requestedCommand: 'forward', failedCommand: 'forward' })}
      />,
    )
    expect(screen.getByText('FAILED')).toBeTruthy()
    expect(screen.queryByText('…')).toBeNull()
  })

  it('shows speed, connection and camera values', () => {
    render(
      <TelemetryPanel
        status={statusWith({ speed: 55, connection: 'connected', camera: 'streaming' })}
      />,
    )
    expect(screen.getByText('55%')).toBeTruthy()
    expect(screen.getByText('CONNECTED')).toBeTruthy()
    expect(screen.getByText('STREAMING')).toBeTruthy()
  })

  it('marks the data as simulated', () => {
    render(<TelemetryPanel status={createInitialDroneStatus()} />)
    expect(screen.getByText(/commands never reach hardware/)).toBeTruthy()
  })
})
