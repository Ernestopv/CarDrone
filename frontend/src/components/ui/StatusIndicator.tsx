import type { StatusTone } from '../../utils/status'

interface StatusIndicatorProps {
  label: string
  tone: StatusTone
  /** Announce status changes to screen readers (e.g. the global connection). */
  live?: boolean
}

const DOT_CLASSES: Record<StatusTone, string> = {
  muted: 'bg-ink-mute',
  warning:
    'bg-caution shadow-[0_0_7px_rgba(242,184,75,0.45)]',
  success: 'bg-ok shadow-[0_0_7px_rgba(74,222,128,0.5)]',
  danger: 'bg-warn shadow-[0_0_7px_rgba(255,82,82,0.5)]',
}

export function StatusIndicator({ label, tone, live = false }: StatusIndicatorProps) {
  // Pulse only live in-progress states (connecting), never steady statuses.
  const pulsing = live && tone === 'warning'

  return (
    <span
      role={live ? 'status' : undefined}
      className="inline-flex items-center gap-2 font-mono text-xs tracking-[0.08em] text-ink"
    >
      <span
        aria-hidden="true"
        className={`h-2 w-2 shrink-0 rounded-full ${DOT_CLASSES[tone]} ${
          pulsing ? 'animate-soft-pulse' : ''
        }`}
      />
      {label}
    </span>
  )
}
