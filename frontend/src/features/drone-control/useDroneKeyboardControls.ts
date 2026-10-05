import { useEffect, useRef } from 'react'
import type { DroneCommand } from '../../types/drone'

export interface UseDroneKeyboardControlsOptions {
  /** Shared command state — the same value the visual pad highlights. */
  requestedCommand: DroneCommand
  /** Begin holding a command (the shared press path; one-shot for `stop`). */
  onPress: (command: DroneCommand) => void
  /** Release the hold (sends `stop` when a movement key was held). */
  onRelease: () => void
}

/**
 * Keyboard key → semantic command. Matched case-insensitively via toLowerCase.
 * Arrow keys are aliases for the four movement commands — same commands,
 * same state, no second command path.
 */
const KEY_COMMANDS: Partial<Record<string, DroneCommand>> = {
  w: 'forward',
  a: 'left',
  s: 'backward',
  d: 'right',
  ' ': 'stop',
  arrowup: 'forward',
  arrowleft: 'left',
  arrowdown: 'backward',
  arrowright: 'right',
}

/** Keys whose browser default is page scrolling; consumed only when handled. */
function scrollsPage(key: string): boolean {
  return key === ' ' || key.startsWith('Arrow')
}

/** The semantic command a key maps to, or null when the key is unbound. */
export function commandForKey(key: string): DroneCommand | null {
  return KEY_COMMANDS[key.toLowerCase()] ?? null
}

// Structural check instead of `instanceof`, so elements from another realm
// still match and the rule stays verifiable without a browser.
function isEditableTarget(target: EventTarget | null): boolean {
  if (!target) return false
  const element = target as HTMLElement
  // Window and document are valid targets too and carry no tagName.
  if (!element.tagName) return false
  if (element.isContentEditable) return true
  const tag = element.tagName.toUpperCase()
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
}

type KeyboardEventDetails = Pick<
  KeyboardEvent,
  'key' | 'repeat' | 'ctrlKey' | 'metaKey' | 'altKey' | 'target'
>

// Pure decision: which semantic command this key press should dispatch, if
// any. Kept separate from the listener so the mapping rules are explicit and
// verifiable. Nothing here touches confirmed state or the connection itself.
export function commandForKeyboardEvent(
  event: KeyboardEventDetails,
  requestedCommand: DroneCommand,
): DroneCommand | null {
  // Auto-repeat while a key is held: the command is already being processed.
  if (event.repeat) return null
  // Leave browser/OS shortcuts (Ctrl+W, Alt+D, ⌘…) to the browser.
  if (event.ctrlKey || event.metaKey || event.altKey) return null
  // Typing stays typing: no drone command from inside an editable element.
  if (isEditableTarget(event.target)) return null

  const command = commandForKey(event.key)
  if (!command) return null
  // Redundant guard: skip a command that is already active. STOP is exempt —
  // it must stay immediately available, exactly like its always-on button.
  if (command === requestedCommand && command !== 'stop') return null
  return command
}

// The listener body, separated from registration so its behaviour — including
// when the default is prevented — stays verifiable without a browser.
// Still just an adapter onto the shared command flow: it never touches
// confirmed state and never re-validates the connection itself.
export function handleDroneKeyDown(
  event: KeyboardEvent,
  requestedCommand: DroneCommand,
  onPress: (command: DroneCommand) => void,
): void {
  const command = commandForKeyboardEvent(event, requestedCommand)
  if (!command) return
  // Consume Space and arrow keys only while they actually drive the drone,
  // so normal page scrolling stays intact everywhere else.
  if (scrollsPage(event.key)) event.preventDefault()
  onPress(command)
}

// Releasing a movement key ends the hold. Space (stop) is one-shot, so its
// keyup is a no-op. A keyup for an unbound key is ignored.
export function handleDroneKeyUp(event: KeyboardEvent, onRelease: () => void): void {
  const command = commandForKey(event.key)
  if (command !== null && command !== 'stop') onRelease()
}

// Keyboard input is only a second input method into the existing flow:
// Keyboard → DroneService → MockDroneService → drone state → UI / Telemetry.
// The visual buttons and the keyboard share one command handler and one state.
export function useDroneKeyboardControls({
  requestedCommand,
  onPress,
  onRelease,
}: UseDroneKeyboardControlsOptions): void {
  // One set of listeners for the component's lifetime, reading the latest
  // values through this ref — re-renders (and StrictMode's double-invoked
  // effects) never leave duplicate listeners behind.
  const latest = useRef({ requestedCommand, onPress, onRelease })
  useEffect(() => {
    latest.current = { requestedCommand, onPress, onRelease }
  })

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) =>
      handleDroneKeyDown(event, latest.current.requestedCommand, latest.current.onPress)
    const handleKeyUp = (event: KeyboardEvent) => handleDroneKeyUp(event, latest.current.onRelease)
    // Losing window focus mid-hold must not leave a movement running.
    const handleBlur = () => latest.current.onRelease()

    window.addEventListener('keydown', handleKeyDown)
    window.addEventListener('keyup', handleKeyUp)
    window.addEventListener('blur', handleBlur)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
      window.removeEventListener('keyup', handleKeyUp)
      window.removeEventListener('blur', handleBlur)
    }
  }, [])
}
