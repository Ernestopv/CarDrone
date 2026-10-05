interface SpeedControlProps {
  value: number
  disabled: boolean
  onChange: (speed: number) => void
}

export function SpeedControl({ value, disabled, onChange }: SpeedControlProps) {
  return (
    <div>
      <div className="flex items-center justify-between">
        <label
          htmlFor="speed-control"
          className="font-mono text-[11px] font-semibold tracking-[0.16em] text-ink-dim"
        >
          SPEED
        </label>
        <span className="font-mono text-sm font-semibold text-hud">{value}%</span>
      </div>
      <input
        id="speed-control"
        type="range"
        min={0}
        max={100}
        step={5}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.currentTarget.valueAsNumber)}
        // After a pointer interaction, drop focus so the arrow keys drive the
        // drone instead of nudging the slider (the reported bug). Keyboard-only
        // users keep focus — no pointer event — and can still adjust it.
        onPointerUp={(event) => event.currentTarget.blur()}
        className="mt-2 w-full accent-hud"
      />
      <div className="mt-1 flex justify-between font-mono text-[10px] tracking-[0.14em] text-ink-mute">
        <span>0</span>
        <span>100%</span>
      </div>
    </div>
  )
}
