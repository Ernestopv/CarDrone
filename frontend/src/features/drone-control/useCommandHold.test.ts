import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { COMMAND_REPEAT_MS, useCommandHold } from './useCommandHold'

describe('useCommandHold', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('sends immediately, re-asserts while held, then stops on release', () => {
    const onCommand = vi.fn()
    const { result } = renderHook(() => useCommandHold(onCommand, true))

    act(() => result.current.press('forward'))
    expect(onCommand).toHaveBeenCalledTimes(1)
    expect(onCommand).toHaveBeenLastCalledWith('forward')

    act(() => vi.advanceTimersByTime(COMMAND_REPEAT_MS * 3))
    // 1 immediate + 3 heartbeats keeps the backend liveness window open.
    expect(onCommand).toHaveBeenCalledTimes(4)
    expect(onCommand).toHaveBeenLastCalledWith('forward')

    act(() => result.current.release())
    expect(onCommand).toHaveBeenLastCalledWith('stop')

    // No heartbeat survives a release.
    const callsAfterRelease = onCommand.mock.calls.length
    act(() => vi.advanceTimersByTime(COMMAND_REPEAT_MS * 2))
    expect(onCommand.mock.calls.length).toBe(callsAfterRelease)
  })

  it('treats stop as a one-shot (no heartbeat)', () => {
    const onCommand = vi.fn()
    const { result } = renderHook(() => useCommandHold(onCommand, true))

    act(() => result.current.press('stop'))
    expect(onCommand).toHaveBeenCalledWith('stop')

    act(() => vi.advanceTimersByTime(COMMAND_REPEAT_MS * 3))
    expect(onCommand).toHaveBeenCalledTimes(1)
  })

  it('does not start a heartbeat for a movement while disabled', () => {
    const onCommand = vi.fn()
    const { result } = renderHook(() => useCommandHold(onCommand, false))

    act(() => result.current.press('forward'))
    expect(onCommand).not.toHaveBeenCalled()

    act(() => vi.advanceTimersByTime(COMMAND_REPEAT_MS * 3))
    expect(onCommand).not.toHaveBeenCalled()
  })

  it('switches the held command without emitting a stop in between', () => {
    const onCommand = vi.fn()
    const { result } = renderHook(() => useCommandHold(onCommand, true))

    act(() => result.current.press('forward'))
    act(() => result.current.press('right'))
    expect(onCommand.mock.calls.map((call) => call[0])).toEqual(['forward', 'right'])

    act(() => vi.advanceTimersByTime(COMMAND_REPEAT_MS))
    // Only the latest held command is re-asserted.
    expect(onCommand).toHaveBeenLastCalledWith('right')
  })
})
