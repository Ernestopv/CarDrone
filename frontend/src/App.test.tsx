import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { App } from './App'
import { droneService } from './services/droneService'

// The dashboard drives the shared service singleton: reset it around every
// test so the integration cases stay order-independent.
beforeEach(async () => {
  await droneService.disconnect()
})

afterEach(async () => {
  await droneService.disconnect()
})

describe('App', () => {
  it('renders the whole dashboard without alerts while healthy', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: 'CAMERA VIEW' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'DRONE CONTROLS' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'SYSTEM STATUS' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'TELEMETRY' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'MOCK SCENARIOS' })).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()

    // Offline: movement unavailable, the rest state still reachable.
    expect(screen.getByRole('button', { name: 'FORWARD' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'STOP' })).toHaveProperty('disabled', false)
  })

  it('connects through the header button and enables the pad', async () => {
    render(<App />)
    fireEvent.click(screen.getByRole('button', { name: 'CONNECT' }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'DISCONNECT' })).toBeTruthy()
    }, { timeout: 3000 })

    expect(screen.getByRole('button', { name: 'FORWARD' })).toHaveProperty('disabled', false)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('surfaces CONNECTION LOST from the scenario and clears it with RESET', async () => {
    render(<App />)
    fireEvent.click(screen.getByRole('button', { name: 'CONNECTION LOST' }))

    await waitFor(() => {
      expect(screen.getByRole('alert').textContent).toContain('CONNECTION LOST')
    })
    // Movement blocked, rest state still available.
    expect(screen.getByRole('button', { name: 'FORWARD' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'STOP' })).toHaveProperty('disabled', false)

    fireEvent.click(screen.getByRole('button', { name: 'RESET' }))
    await waitFor(() => {
      expect(screen.queryByRole('alert')).toBeNull()
    })
    expect(screen.getByRole('button', { name: 'FORWARD' })).toHaveProperty('disabled', false)
  })
})
