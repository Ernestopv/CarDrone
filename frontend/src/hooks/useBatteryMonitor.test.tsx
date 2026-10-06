import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useBatteryMonitor } from './useBatteryMonitor'
import { droneService } from '../services/droneService'

// Minimal harness exposing the hook state as text (specs/hardware/battery-monitoring.md).
function Harness() {
  const { status } = useBatteryMonitor()
  return (
    <div>
      <span data-testid="available">{String(status.available)}</span>
      <span data-testid="voltage">{status.voltage ?? 'null'}</span>
      <span data-testid="percent">{status.percent ?? 'null'}</span>
      <span data-testid="state">{status.state}</span>
      <span data-testid="simulated">{String(status.simulated)}</span>
    </div>
  )
}

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('useBatteryMonitor', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('loads the battery on mount and keeps polling on a fixed cadence', async () => {
    const spy = vi.spyOn(droneService, 'getBattery').mockResolvedValue({
      available: true,
      voltage: 7.81,
      percent: 65,
      state: 'ok',
      simulated: false,
      current: -0.45,
      power: -3.51,
    })

    render(<Harness />)
    await act(async () => {
      await Promise.resolve()
    })
    expect(spy).toHaveBeenCalledTimes(1)
    expect(screen.getByTestId('available').textContent).toBe('true')
    expect(screen.getByTestId('voltage').textContent).toBe('7.81')
    expect(screen.getByTestId('simulated').textContent).toBe('false')

    act(() => {
      vi.advanceTimersByTime(5000)
    })
    expect(spy).toHaveBeenCalledTimes(2)
  })

  it('surfaces the error state when the service rejects', async () => {
    vi.spyOn(droneService, 'getBattery').mockRejectedValue(new Error('sensor down'))

    render(<Harness />)
    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.getByTestId('available').textContent).toBe('false')
    expect(screen.getByTestId('state').textContent).toBe('error')
  })

  it('clears the polling timer on unmount', async () => {
    const spy = vi.spyOn(droneService, 'getBattery').mockResolvedValue({
      available: true,
      voltage: 7.8,
      percent: 64,
      state: 'ok',
      simulated: true,
      current: -0.45,
      power: -3.51,
    })

    const { unmount } = render(<Harness />)
    await act(async () => {
      await Promise.resolve()
    })
    expect(spy).toHaveBeenCalledTimes(1)

    unmount()
    act(() => {
      vi.advanceTimersByTime(50_000)
    })
    expect(spy).toHaveBeenCalledTimes(1)
  })
})