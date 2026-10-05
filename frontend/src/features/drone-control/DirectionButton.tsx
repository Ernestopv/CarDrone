import type { DroneCommand } from '../../types/drone'

interface DirectionButtonProps {
  command: DroneCommand
  glyph: string
  label: string
  active: boolean
  disabled: boolean
  variant?: 'direction' | 'stop'
  className?: string
  onPress: (command: DroneCommand) => void
  onRelease: () => void
}

const BASE_CLASSES =
  'flex h-16 flex-col items-center justify-center gap-1 rounded-[3px] border font-mono text-[11px] font-semibold tracking-[0.14em] transition-colors disabled:cursor-not-allowed disabled:opacity-40'

// Cyan marks the engaged control; red is reserved for STOP.
const VARIANT_CLASSES = {
  direction: {
    active: 'border-hud bg-hud/10 text-hud',
    idle: 'border-rule bg-night/40 text-ink-dim hover:border-hud/50 hover:text-hud',
  },
  stop: {
    active: 'border-warn bg-warn/15 text-warn',
    idle: 'border-warn/40 bg-night/40 text-warn hover:border-warn/70 hover:bg-warn/10',
  },
} as const

export function DirectionButton({
  command,
  glyph,
  label,
  active,
  disabled,
  variant = 'direction',
  className = '',
  onPress,
  onRelease,
}: DirectionButtonProps) {
  const classes = VARIANT_CLASSES[variant]

  return (
    <button
      type="button"
      // Hold-to-move: press starts (and keeps re-asserting) the command;
      // release stops. Pointer capture keeps the matching pointerup on this
      // button even when the finger/cursor leaves it before the user lets go.
      onPointerDown={(event) => {
        const target = event.currentTarget
        if (typeof target.setPointerCapture === 'function' && typeof event.pointerId === 'number') {
          target.setPointerCapture(event.pointerId)
        }
        onPress(command)
      }}
      onPointerUp={() => onRelease()}
      onPointerCancel={() => onRelease()}
      // Keyboard activation of a focused button: Enter holds the command while
      // pressed; Space is the global STOP key (handled by the keyboard hook).
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault()
          onPress(command)
        }
      }}
      onKeyUp={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault()
          onRelease()
        }
      }}
      disabled={disabled}
      aria-pressed={active}
      className={`${BASE_CLASSES} ${active ? classes.active : classes.idle} ${className}`}
    >
      <span aria-hidden="true" className="text-base leading-none">
        {glyph}
      </span>
      {label}
    </button>
  )
}
