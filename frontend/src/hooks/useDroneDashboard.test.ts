import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { droneService } from '../services/droneService'
import { useDroneDashboard } from './useDroneDashboard'

// The dashboard drives the shared service singleton: start every test from a
// known point so the suite stays order-independent.
beforeEach(async () => {
  await droneService.disconnect()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('useDroneDashboard', () => {
  it('starts offline with a normalized status', () => {
    const { result } = renderHook(() => useDroneDashboard())
    expect(result.current.status).toMatchObject({
      connection: 'offline',
      camera: 'offline',
      requestedCommand: 'stop',
      confirmedCommand: 'stop',
      failedCommand: null,
      speed: 0,
    })
    expect(result.current.connectionPending).toBe(false)
  })

  it('moves through pending to connected and back to offline', async () => {
    const { result } = renderHook(() => useDroneDashboard())

    let attempt: Promise<void> = Promise.resolve()
    act(() => {
      attempt = result.current.toggleConnection()
    })
    expect(result.current.connectionPending).toBe(true)
    expect(result.current.status.connection).toBe('connecting')

    await act(async () => {
      await attempt
    })
    expect(result.current.connectionPending).toBe(false)
    expect(result.current.status.connection).toBe('connected')
    expect(result.current.status.camera).toBe('streaming')

    await act(async () => {
      await result.current.toggleConnection()
    })
    expect(result.current.status.connection).toBe('offline')
    expect(result.current.connectionPending).toBe(false)
  })

  it('records an optimistic request and confirms only after the ack', async () => {
    const { result } = renderHook(() => useDroneDashboard())
    await act(async () => {
      await result.current.toggleConnection()
    })

    let command: Promise<void> = Promise.resolve()
    act(() => {
      command = result.current.sendCommand('forward')
    })
    // Request moves immediately; confirmation waits for the simulated ack.
    expect(result.current.status.requestedCommand).toBe('forward')
    expect(result.current.status.confirmedCommand).toBe('stop')

    await act(async () => {
      await command
    })
    expect(result.current.status.confirmedCommand).toBe('forward')
    expect(result.current.status.failedCommand).toBeNull()
    expect(result.current.status.connection).toBe('connected')
  })

  it('ignores movement while offline', () => {
    const { result } = renderHook(() => useDroneDashboard())
    void result.current.sendCommand('forward')
    expect(result.current.status.requestedCommand).toBe('stop')
    expect(result.current.status.confirmedCommand).toBe('stop')
  })

  it('records STOP while offline without confirming anything', async () => {
    const { result } = renderHook(() => useDroneDashboard())
    await act(async () => {
      await result.current.sendCommand('stop')
    })
    expect(result.current.status.requestedCommand).toBe('stop')
    expect(result.current.status.confirmedCommand).toBe('stop')
    expect(result.current.status.connection).toBe('offline')
  })

  it('records a rejected command without confirming it or breaking the link', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const { result } = renderHook(() => useDroneDashboard())
    await act(async () => {
      await result.current.toggleConnection()
    })
    await act(async () => {
      await result.current.runScenario('commandFailure')
    })

    await act(async () => {
      await result.current.sendCommand('forward')
    })
    expect(result.current.status.failedCommand).toBe('forward')
    expect(result.current.status.requestedCommand).toBe('forward')
    expect(result.current.status.confirmedCommand).toBe('stop')
    expect(result.current.status.connection).toBe('connected')

    // A follow-up command succeeds and clears the recorded failure.
    await act(async () => {
      await result.current.sendCommand('left')
    })
    expect(result.current.status.failedCommand).toBeNull()
    expect(result.current.status.confirmedCommand).toBe('left')
  })

  it('applies scenarios and publishes their status', async () => {
    const { result } = renderHook(() => useDroneDashboard())
    await act(async () => {
      await result.current.toggleConnection()
    })

    await act(async () => {
      await result.current.runScenario('apiUnavailable')
    })
    expect(result.current.status.services.api).toBe('error')

    await act(async () => {
      await result.current.runScenario('reset')
    })
    expect(result.current.status.services.api).toBe('online')
    expect(result.current.status.connection).toBe('connected')
  })

  it('updates speed with clamping', () => {
    const { result } = renderHook(() => useDroneDashboard())
    act(() => {
      result.current.changeSpeed(150)
    })
    expect(result.current.status.speed).toBe(100)
    act(() => {
      result.current.changeSpeed(-10)
    })
    expect(result.current.status.speed).toBe(0)
    act(() => {
      result.current.changeSpeed(45)
    })
    expect(result.current.status.speed).toBe(45)
  })
})
