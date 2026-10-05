import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useCameraStream } from './useCameraStream'

// Minimal harness so the hook's backoff/cancellation behavior is tested
// directly (specs/integration/camera-stream.md).
function Harness({ enabled }: { enabled: boolean }) {
  const { phase, frameVersion, onFrame, onFeedError } = useCameraStream(enabled)
  return (
    <div>
      <span data-testid="phase">{phase}</span>
      <span data-testid="version">{frameVersion}</span>
      <button data-testid="error" onClick={onFeedError} />
      <button data-testid="load" onClick={onFrame} />
    </div>
  )
}

function phase() {
  return screen.getByTestId('phase').textContent
}

function version() {
  return Number(screen.getByTestId('version').textContent)
}

afterEach(() => {
  vi.useRealTimers()
})

describe('useCameraStream', () => {
  it('starts loading when enabled and streaming', () => {
    render(<Harness enabled />)
    expect(phase()).toBe('loading')
  })

  it('parks as idle while disabled', () => {
    render(<Harness enabled={false} />)
    expect(phase()).toBe('idle')
  })

  it('reconnects with bounded backoff and cancels when disabled', () => {
    vi.useFakeTimers()
    const { rerender } = render(<Harness enabled />)
    expect(version()).toBe(0)

    // Error 1 → delay 1000ms → version 1
    fireEvent.click(screen.getByTestId('error'))
    expect(phase()).toBe('error')
    act(() => {
      vi.advanceTimersByTime(1000)
    })
    expect(version()).toBe(1)
    expect(phase()).toBe('loading')

    // Error 2 → delay 2000ms
    fireEvent.click(screen.getByTestId('error'))
    act(() => {
      vi.advanceTimersByTime(2000)
    })
    expect(version()).toBe(2)

    // Error 3 → delay 4000ms
    fireEvent.click(screen.getByTestId('error'))
    act(() => {
      vi.advanceTimersByTime(4000)
    })
    expect(version()).toBe(3)

    // Error 4 → capped at 5000ms
    fireEvent.click(screen.getByTestId('error'))
    act(() => {
      vi.advanceTimersByTime(5000)
    })
    expect(version()).toBe(4)

    // A successful first frame resets both the phase and the backoff ladder.
    fireEvent.click(screen.getByTestId('load'))
    expect(phase()).toBe('live')
    fireEvent.click(screen.getByTestId('error'))
    act(() => {
      vi.advanceTimersByTime(1000)
    })
    expect(version()).toBe(5)

    // Disabling cancels the pending retry entirely.
    act(() => {
      rerender(<Harness enabled={false} />)
    })
    expect(phase()).toBe('idle')
    const before = version()
    act(() => {
      vi.advanceTimersByTime(20_000)
    })
    expect(version()).toBe(before)
  })
})