import { useId } from 'react'
import type { ReactNode } from 'react'

interface PanelProps {
  title: string
  children: ReactNode
  className?: string
}

export function Panel({ title, children, className = '' }: PanelProps) {
  const titleId = useId()

  return (
    <section
      aria-labelledby={titleId}
      className={`flex min-w-0 flex-col bg-panel ${className}`}
    >
      <header className="flex items-center gap-3 px-4 pt-4 pb-3">
        <h2
          id={titleId}
          className="font-mono text-[11px] font-semibold tracking-[0.16em] text-ink-dim"
        >
          {title}
        </h2>
        <span aria-hidden="true" className="h-px flex-1 bg-rule" />
      </header>
      <div className="flex-1 px-4 pb-4">{children}</div>
    </section>
  )
}
