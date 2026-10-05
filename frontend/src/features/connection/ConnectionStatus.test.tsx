import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ConnectionStatus } from './ConnectionStatus'

describe('ConnectionStatus', () => {
  it('shows the offline label and a CONNECT button', () => {
    const onToggle = vi.fn()
    render(<ConnectionStatus connection="offline" pending={false} onToggle={onToggle} />)
    expect(screen.getByText('DISCONNECTED')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'CONNECT' }))
    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('shows connecting and disables the button while pending', () => {
    render(<ConnectionStatus connection="connecting" pending onToggle={vi.fn()} />)
    expect(screen.getByText('CONNECTING')).toBeTruthy()
    expect(screen.getByRole('button')).toHaveProperty('disabled', true)
  })

  it('shows connected with a DISCONNECT button', () => {
    render(<ConnectionStatus connection="connected" pending={false} onToggle={vi.fn()} />)
    expect(screen.getByText('CONNECTED')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'DISCONNECT' })).toBeTruthy()
  })

  it('shows the error label', () => {
    render(<ConnectionStatus connection="error" pending={false} onToggle={vi.fn()} />)
    expect(screen.getByText('ERROR')).toBeTruthy()
  })

  it('announces the connection state to assistive technology', () => {
    render(<ConnectionStatus connection="connected" pending={false} onToggle={vi.fn()} />)
    expect(screen.getByRole('status').textContent).toContain('CONNECTED')
  })
})
