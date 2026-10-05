import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SystemStatusPanel } from './SystemStatusPanel'

describe('SystemStatusPanel', () => {
  it('lists all four services with their current values', () => {
    render(
      <SystemStatusPanel
        services={{ drone: 'online', raspberryPi: 'offline', camera: 'error', api: 'connecting' }}
      />,
    )
    expect(screen.getByText('Drone')).toBeTruthy()
    expect(screen.getByText('Raspberry Pi')).toBeTruthy()
    expect(screen.getByText('Camera')).toBeTruthy()
    expect(screen.getByText('Backend API')).toBeTruthy()
    expect(screen.getByText('Online')).toBeTruthy()
    expect(screen.getByText('Offline')).toBeTruthy()
    expect(screen.getByText('Error')).toBeTruthy()
    expect(screen.getByText('Connecting')).toBeTruthy()
  })

  it('shows every service offline when the app starts', () => {
    render(
      <SystemStatusPanel
        services={{ drone: 'offline', raspberryPi: 'offline', camera: 'offline', api: 'offline' }}
      />,
    )
    expect(screen.getAllByText('Offline')).toHaveLength(4)
  })
})
