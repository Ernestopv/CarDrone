import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { DroneStatus } from '../../types/drone'
import { createInitialDroneStatus } from '../../utils/status'
import { SystemAlerts } from './SystemAlerts'

describe('SystemAlerts', () => {
  it('renders nothing when healthy', () => {
    const { container } = render(<SystemAlerts status={createInitialDroneStatus()} />)
    expect(container.firstChild).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows CONNECTION LOST as a readable alert', () => {
    const status: DroneStatus = { ...createInitialDroneStatus(), connection: 'error' }
    render(<SystemAlerts status={status} />)
    const alert = screen.getByRole('alert')
    expect(alert.textContent).toContain('CONNECTION LOST')
    expect(alert.textContent).toContain('Reconnect to resume control')
  })

  it('suppresses the camera alert while the connection is lost', () => {
    const status: DroneStatus = { ...createInitialDroneStatus(), connection: 'error' }
    render(<SystemAlerts status={status} />)
    expect(screen.queryByText('CAMERA UNAVAILABLE')).toBeNull()
  })

  it('shows BACKEND API UNAVAILABLE for an API outage', () => {
    const status: DroneStatus = {
      ...createInitialDroneStatus(),
      services: { ...createInitialDroneStatus().services, api: 'error' },
    }
    render(<SystemAlerts status={status} />)
    expect(screen.getByRole('alert').textContent).toContain('BACKEND API UNAVAILABLE')
  })

  it('shows CAMERA UNAVAILABLE while the drone stays connected', () => {
    const initial = createInitialDroneStatus()
    const status: DroneStatus = {
      ...initial,
      connection: 'connected',
      camera: 'error',
      services: { ...initial.services, drone: 'online', camera: 'error', api: 'online' },
    }
    render(<SystemAlerts status={status} />)
    expect(screen.getByRole('alert').textContent).toContain('CAMERA UNAVAILABLE')
  })

  it('names the failed command', () => {
    const status: DroneStatus = { ...createInitialDroneStatus(), failedCommand: 'forward' }
    render(<SystemAlerts status={status} />)
    const alert = screen.getByRole('alert')
    expect(alert.textContent).toContain('COMMAND FAILED')
    expect(alert.textContent).toContain('FORWARD was requested but not confirmed')
  })

  it('shows independent failures together, without the redundant camera alert', () => {
    const initial = createInitialDroneStatus()
    const status: DroneStatus = {
      ...initial,
      connection: 'error',
      failedCommand: 'left',
      services: { ...initial.services, api: 'error' },
    }
    render(<SystemAlerts status={status} />)
    const texts = screen.getAllByRole('alert').map((alert) => alert.textContent)
    expect(texts).toHaveLength(3)
    expect(texts.some((text) => text?.includes('CONNECTION LOST'))).toBe(true)
    expect(texts.some((text) => text?.includes('BACKEND API UNAVAILABLE'))).toBe(true)
    expect(texts.some((text) => text?.includes('COMMAND FAILED'))).toBe(true)
    expect(texts.some((text) => text?.includes('CAMERA UNAVAILABLE'))).toBe(false)
  })
})
