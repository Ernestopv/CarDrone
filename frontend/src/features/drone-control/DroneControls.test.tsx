import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { DroneCommand } from '../../types/drone'
import { DroneControls } from './DroneControls'

interface Overrides {
  connected?: boolean
  requestedCommand?: DroneCommand
  speed?: number
}

function renderControls(overrides: Overrides = {}) {
  const onCommand = vi.fn()
  const onSpeedChange = vi.fn()
  render(
    <DroneControls
      connected={overrides.connected ?? false}
      requestedCommand={overrides.requestedCommand ?? 'stop'}
      speed={overrides.speed ?? 0}
      onCommand={onCommand}
      onSpeedChange={onSpeedChange}
    />,
  )
  return { onCommand, onSpeedChange }
}

const MOVEMENT = ['FORWARD', 'LEFT', 'RIGHT', 'BACKWARD']

describe('DroneControls', () => {
  it('disables movement while offline but keeps STOP available', () => {
    renderControls()
    for (const name of MOVEMENT) {
      expect(screen.getByRole('button', { name })).toHaveProperty('disabled', true)
    }
    expect(screen.getByRole('button', { name: 'STOP' })).toHaveProperty('disabled', false)
  })

  it('enables every pad button while connected', () => {
    renderControls({ connected: true })
    for (const name of [...MOVEMENT, 'STOP']) {
      expect(screen.getByRole('button', { name })).toHaveProperty('disabled', false)
    }
  })

  it('sends its command on click', () => {
    const { onCommand } = renderControls({ connected: true })
    fireEvent.click(screen.getByRole('button', { name: 'FORWARD' }))
    expect(onCommand).toHaveBeenCalledWith('forward')
    fireEvent.click(screen.getByRole('button', { name: 'STOP' }))
    expect(onCommand).toHaveBeenCalledWith('stop')
  })

  it('exposes the active command via aria-pressed', () => {
    renderControls({ connected: true, requestedCommand: 'forward' })
    expect(screen.getByRole('button', { name: 'FORWARD' }).getAttribute('aria-pressed')).toBe(
      'true',
    )
    expect(screen.getByRole('button', { name: 'LEFT' }).getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByRole('button', { name: 'STOP' }).getAttribute('aria-pressed')).toBe('false')
  })

  it('disables the speed slider while offline', () => {
    renderControls()
    expect(screen.getByLabelText('SPEED')).toHaveProperty('disabled', true)
  })

  it('reports speed changes while connected', () => {
    const { onSpeedChange } = renderControls({ connected: true })
    fireEvent.change(screen.getByLabelText('SPEED'), { target: { value: '55' } })
    expect(onSpeedChange).toHaveBeenCalledWith(55)
  })

  it('shows the keyboard hint', () => {
    renderControls()
    expect(screen.getByText(/SPACE STOP/)).toBeTruthy()
  })
})
