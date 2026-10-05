import type { DroneCommand } from '../../types/drone'

interface DirectionButtonProps {
  command: DroneCommand
  glyph: string
  label: string
  active: boolean
  disabled: boolean
  variant?: 'direction' | 'stop'
  className?: string
  onCommand: (command: DroneCommand) => void
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
  onCommand,
}: DirectionButtonProps) {
  const classes = VARIANT_CLASSES[variant]

  return (
    <button
      type="button"
      onClick={() => onCommand(command)}
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
