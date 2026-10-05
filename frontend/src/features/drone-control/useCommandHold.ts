import { useCallback, useEffect, useRef } from 'react'
import type { DroneCommand } from '../../types/drone'

/**
 * How often a held movement command is re-asserted. The backend safety path
 * gives every movement command a bounded liveness window
 * (`Safety:CommandTimeoutMilliseconds`, default `2000` ms) and drives a
 * semantic STOP when no fresh command arrives in time, so a hold must re-send
 * within that window to stay active. Kept comfortably below the default.
 */
export const COMMAND_REPEAT_MS = 1000

export interface CommandHold {
  /** Begin holding a command. `stop` is one-shot (sent immediately, no hold). */
  press: (command: DroneCommand) => void
  /** End the hold: stop the heartbeat and send `stop` if a movement was held. */
  release: () => void
}

/**
 * Turns a press/release pair (button or key) into a re-asserted movement
 * command. While a movement control is held the command is re-sent every
 * {@link COMMAND_REPEAT_MS}, so the backend liveness window never expires and
 * the drone keeps moving; releasing — or losing the connection / unmounting —
 * sends a single `stop`.
 *
 * The latest `onCommand`/`enabled` are read through refs, so `press`/`release`
 * keep stable identities and a running heartbeat never captures stale state.
 */
export function useCommandHold(
  onCommand: (command: DroneCommand) => void,
  enabled: boolean,
): CommandHold {
  const timer = useRef<number | undefined>(undefined)
  const held = useRef<DroneCommand | null>(null)
  const onCommandRef = useRef(onCommand)
  const enabledRef = useRef(enabled)

  useEffect(() => {
    onCommandRef.current = onCommand
    enabledRef.current = enabled
  })

  const clear = useCallback(() => {
    if (timer.current !== undefined) {
      window.clearInterval(timer.current)
      timer.current = undefined
    }
  }, [])

  const release = useCallback(() => {
    if (held.current === null) return
    held.current = null
    clear()
    onCommandRef.current('stop')
  }, [clear])

  const press = useCallback(
    (command: DroneCommand) => {
      clear()
      if (command === 'stop') {
        // STOP is always a one-shot: it releases any hold and stops now.
        held.current = null
        onCommandRef.current('stop')
        return
      }
      // Movement while the drone is not connected is ignored (the service
      // would drop it too); never start a heartbeat for it.
      if (!enabledRef.current) return
      held.current = command
      onCommandRef.current(command)
      timer.current = window.setInterval(() => {
        if (held.current !== null) onCommandRef.current(held.current)
      }, COMMAND_REPEAT_MS)
    },
    [clear],
  )

  // Losing the connection must stop the heartbeat (no command is sent: the
  // service already ignores movement while disconnected).
  useEffect(() => {
    if (!enabled) {
      held.current = null
      clear()
    }
  }, [enabled, clear])

  // Unmount: never leave a timer running.
  useEffect(() => clear, [clear])

  return { press, release }
}
