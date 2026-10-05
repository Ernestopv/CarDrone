import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CameraStatus } from '../../types/drone'
import { CAMERA_STREAM_PATH } from './useCameraStream'
import { CameraView } from './CameraView'

function renderCamera(
  status: CameraStatus,
  overrides: { cameraMode?: 'mock' | 'ustreamer' | null; cameraUnavailable?: boolean } = {},
) {
  return render(
    <CameraView
      status={status}
      confirmedCommand="forward"
      speed={40}
      cameraMode={overrides.cameraMode ?? 'mock'}
      cameraUnavailable={overrides.cameraUnavailable}
    />,
  )
}

afterEach(() => {
  vi.useRealTimers()
})

describe('CameraView — mock mode', () => {
  it('shows NO SIGNAL while offline', () => {
    renderCamera('offline')
    expect(screen.getByText('NO SIGNAL')).toBeTruthy()
    expect(screen.getByText(/Connect the drone/)).toBeTruthy()
    expect(document.querySelector('img')).toBeNull()
  })

  it('shows CONNECTING... while connecting', () => {
    renderCamera('connecting')
    expect(screen.getByText('CONNECTING...')).toBeTruthy()
  })

  it('shows a simulated stream while streaming', () => {
    renderCamera('streaming')
    expect(screen.getByText('SIMULATED STREAM')).toBeTruthy()
    expect(screen.getByText(/no hardware is attached/)).toBeTruthy()
    // Mock mode never mounts the real feed.
    expect(document.querySelector('img')).toBeNull()
  })

  it('shows STREAM ERROR on failure', () => {
    renderCamera('error')
    expect(screen.getByText('STREAM ERROR')).toBeTruthy()
  })

  it('falls back to a failure state for an unknown camera state', () => {
    const unknown = 'something-else' as unknown as CameraStatus
    renderCamera(unknown)
    expect(screen.getByText('STREAM ERROR')).toBeTruthy()
  })

  it('exposes the state text to assistive technology', () => {
    renderCamera('streaming')
    expect(screen.getByRole('status').textContent).toBe('SIMULATED STREAM')
  })

  it('shows the confirmed command and speed in the OSD', () => {
    renderCamera('streaming')
    expect(screen.getByText('FORWARD')).toBeTruthy()
    expect(screen.getByText('40%')).toBeTruthy()
  })
})

describe('CameraView — ustreamer mode', () => {
  it('mounts the real feed only while streaming', () => {
    const { container } = render(
      <CameraView
        status="streaming"
        confirmedCommand="forward"
        speed={40}
        cameraMode="ustreamer"
      />,
    )
    const img = container.querySelector('img')
    expect(img?.getAttribute('src')).toBe(CAMERA_STREAM_PATH)
    // No simulated label can leak into real mode.
    expect(screen.queryByText('SIMULATED STREAM')).toBeNull()
  })

  it('offline/connecting/error statuses show honest overlays and no feed', () => {
    const offline = renderCamera('offline', { cameraMode: 'ustreamer' })
    expect(screen.getByText('NO SIGNAL')).toBeTruthy()
    expect(offline.container.querySelector('img')).toBeNull()

    const connecting = renderCamera('connecting', { cameraMode: 'ustreamer' })
    expect(screen.getByText('CONNECTING...')).toBeTruthy()
    expect(connecting.container.querySelector('img')).toBeNull()

    const error = renderCamera('error', { cameraMode: 'ustreamer' })
    expect(screen.getByText('STREAM ERROR')).toBeTruthy()
    expect(error.container.querySelector('img')).toBeNull()
    expect(document.querySelectorAll('img').length).toBe(0)
  })

  it('drops the overlay once a first frame renders (no text over the feed)', () => {
    const { container } = render(
      <CameraView status="streaming" confirmedCommand="forward" speed={40} cameraMode="ustreamer" />,
    )
    expect(screen.getByText('CONNECTING...')).toBeTruthy()
    const img = container.querySelector('img')
    expect(img).toBeTruthy()

    fireEvent.load(img!)
    // Live: the feed renders with no overlay text at all.
    expect(screen.queryByText('LIVE FEED')).toBeNull()
    expect(screen.queryByText('CONNECTING...')).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('reconnects with backoff after a feed error while streaming', () => {
    vi.useFakeTimers()
    const { container } = render(
      <CameraView status="streaming" confirmedCommand="forward" speed={40} cameraMode="ustreamer" />,
    )
    const first = container.querySelector('img')
    expect(first).toBeTruthy()

    fireEvent.error(first!)
    expect(screen.getByText('STREAM ERROR')).toBeTruthy()

    // After the 1s backoff a fresh <img> (fresh GET) is mounted.
    act(() => {
      vi.advanceTimersByTime(1000)
    })
    const second = container.querySelector('img')
    expect(second).toBeTruthy()
    expect(second).not.toBe(first)

    fireEvent.load(second!)
    // Live again: the grace error overlay is gone.
    expect(screen.queryByText('STREAM ERROR')).toBeNull()
    expect(container.querySelector('img')).toBeTruthy()
  })

  it('stops retrying when the status departs streaming', () => {
    vi.useFakeTimers()
    const { container, rerender } = render(
      <CameraView status="streaming" confirmedCommand="forward" speed={40} cameraMode="ustreamer" />,
    )
    fireEvent.error(container.querySelector('img')!)
    expect(screen.getByText('STREAM ERROR')).toBeTruthy()

    // Status leaves streaming before the retry fires: feed gone, no timer tick.
    rerender(
      <CameraView status="error" confirmedCommand="forward" speed={40} cameraMode="ustreamer" />,
    )
    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByText('STREAM ERROR')).toBeTruthy()
    act(() => {
      vi.advanceTimersByTime(10_000)
    })
    // No new feed attempt appeared after the (cancelled) backoff window.
    expect(container.querySelector('img')).toBeNull()
  })

  it('issues a fresh feed when the status returns to streaming', () => {
    const { container, rerender } = render(
      <CameraView status="error" confirmedCommand="forward" speed={40} cameraMode="ustreamer" />,
    )
    expect(container.querySelector('img')).toBeNull()

    rerender(
      <CameraView status="streaming" confirmedCommand="forward" speed={40} cameraMode="ustreamer" />,
    )
    expect(container.querySelector('img')?.getAttribute('src')).toBe(CAMERA_STREAM_PATH)
  })
})

describe('CameraView — mode resolution', () => {
  it('shows a neutral state while the mode is unresolved (never simulated)', () => {
    const { container } = render(
      <CameraView status="streaming" confirmedCommand="forward" speed={40} cameraMode={null} />,
    )
    expect(screen.getByText('CHECKING…')).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
    expect(screen.queryByText('SIMULATED STREAM')).toBeNull()
  })

  it('shows an honest unavailable state when mode resolution failed', () => {
    const { container } = render(
      <CameraView
        status="streaming"
        confirmedCommand="forward"
        speed={40}
        cameraMode={null}
        cameraUnavailable
      />,
    )
    expect(screen.getByText('CAMERA UNAVAILABLE')).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
    expect(screen.queryByText('SIMULATED STREAM')).toBeNull()
  })
})