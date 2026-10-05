import { renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  commandForKeyboardEvent,
  handleDroneKeyDown,
  useDroneKeyboardControls,
} from './useDroneKeyboardControls'

function keyEvent(key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  return new KeyboardEvent('keydown', { key, cancelable: true, ...init })
}

function dispatchedOn(element: Element, key: string): KeyboardEvent {
  const event = keyEvent(key)
  element.dispatchEvent(event)
  return event
}

describe('commandForKeyboardEvent', () => {
  it('maps W/A/S/D to the four movement commands', () => {
    expect(commandForKeyboardEvent(keyEvent('w'), 'stop')).toBe('forward')
    expect(commandForKeyboardEvent(keyEvent('a'), 'stop')).toBe('left')
    expect(commandForKeyboardEvent(keyEvent('s'), 'stop')).toBe('backward')
    expect(commandForKeyboardEvent(keyEvent('d'), 'stop')).toBe('right')
  })

  it('maps keys case-insensitively', () => {
    expect(commandForKeyboardEvent(keyEvent('W'), 'stop')).toBe('forward')
    expect(commandForKeyboardEvent(keyEvent('D'), 'stop')).toBe('right')
  })

  it('maps arrow keys as aliases for movement', () => {
    expect(commandForKeyboardEvent(keyEvent('ArrowUp'), 'stop')).toBe('forward')
    expect(commandForKeyboardEvent(keyEvent('ArrowLeft'), 'stop')).toBe('left')
    expect(commandForKeyboardEvent(keyEvent('ArrowDown'), 'stop')).toBe('backward')
    expect(commandForKeyboardEvent(keyEvent('ArrowRight'), 'stop')).toBe('right')
  })

  it('maps Space to stop', () => {
    expect(commandForKeyboardEvent(keyEvent(' '), 'stop')).toBe('stop')
  })

  it('ignores auto-repeat while a key is held', () => {
    expect(commandForKeyboardEvent(keyEvent('w', { repeat: true }), 'stop')).toBeNull()
  })

  it('leaves browser and OS shortcuts alone', () => {
    expect(commandForKeyboardEvent(keyEvent('w', { ctrlKey: true }), 'stop')).toBeNull()
    expect(commandForKeyboardEvent(keyEvent('w', { metaKey: true }), 'stop')).toBeNull()
    expect(commandForKeyboardEvent(keyEvent('w', { altKey: true }), 'stop')).toBeNull()
  })

  it('ignores unmapped keys', () => {
    expect(commandForKeyboardEvent(keyEvent('q'), 'stop')).toBeNull()
    expect(commandForKeyboardEvent(keyEvent('Enter'), 'stop')).toBeNull()
  })

  it('skips a command that is already active, with stop exempt', () => {
    expect(commandForKeyboardEvent(keyEvent('w'), 'forward')).toBeNull()
    expect(commandForKeyboardEvent(keyEvent('ArrowUp'), 'forward')).toBeNull()
    expect(commandForKeyboardEvent(keyEvent(' '), 'stop')).toBe('stop')
  })

  it('ignores keys typed inside form fields', () => {
    for (const tag of ['input', 'textarea', 'select']) {
      const element = document.createElement(tag)
      document.body.appendChild(element)
      expect(commandForKeyboardEvent(dispatchedOn(element, 'w'), 'stop')).toBeNull()
      element.remove()
    }
  })

  it('ignores keys inside content-editable elements', () => {
    const element = document.createElement('div')
    Object.defineProperty(element, 'isContentEditable', { value: true })
    document.body.appendChild(element)
    expect(commandForKeyboardEvent(dispatchedOn(element, 'w'), 'stop')).toBeNull()
    element.remove()
  })
})

describe('handleDroneKeyDown', () => {
  it('dispatches and consumes Space', () => {
    const onCommand = vi.fn()
    const event = keyEvent(' ')
    handleDroneKeyDown(event, 'stop', onCommand)
    expect(onCommand).toHaveBeenCalledTimes(1)
    expect(onCommand).toHaveBeenCalledWith('stop')
    expect(event.defaultPrevented).toBe(true)
  })

  it('consumes arrow keys while they drive the drone', () => {
    const onCommand = vi.fn()
    const event = keyEvent('ArrowUp')
    handleDroneKeyDown(event, 'stop', onCommand)
    expect(onCommand).toHaveBeenCalledWith('forward')
    expect(event.defaultPrevented).toBe(true)
  })

  it('leaves page scrolling alone for unmapped keys', () => {
    const onCommand = vi.fn()
    const event = keyEvent('q')
    handleDroneKeyDown(event, 'stop', onCommand)
    expect(onCommand).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
  })

  it('does not consume a key whose command is already active', () => {
    const onCommand = vi.fn()
    const event = keyEvent('ArrowDown')
    handleDroneKeyDown(event, 'backward', onCommand)
    expect(onCommand).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
  })

  it('never prevents the default for handled letter keys', () => {
    const onCommand = vi.fn()
    const event = keyEvent('w')
    handleDroneKeyDown(event, 'stop', onCommand)
    expect(onCommand).toHaveBeenCalledWith('forward')
    expect(event.defaultPrevented).toBe(false)
  })
})

describe('useDroneKeyboardControls', () => {
  it('drives onCommand from one window listener and removes it on unmount', () => {
    const onCommand = vi.fn()
    const { unmount } = renderHook(() =>
      useDroneKeyboardControls({ requestedCommand: 'stop', onCommand }),
    )

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'w' }))
    expect(onCommand).toHaveBeenCalledTimes(1)
    expect(onCommand).toHaveBeenCalledWith('forward')

    unmount()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }))
    expect(onCommand).toHaveBeenCalledTimes(1)
  })
})
