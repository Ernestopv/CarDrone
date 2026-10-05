import { Panel } from '../../components/ui/Panel'
import type { DroneCommand } from '../../types/drone'
import { DirectionButton } from './DirectionButton'
import { SpeedControl } from './SpeedControl'

interface DroneControlsProps {
  connected: boolean
  /** The pad highlights the command the user has requested. */
  requestedCommand: DroneCommand
  speed: number
  /** Press/begin a command hold (one-shot for `stop`). */
  onPress: (command: DroneCommand) => void
  /** Release the hold (sends `stop` when a movement was held). */
  onRelease: () => void
  onSpeedChange: (speed: number) => void
}

interface PadButtonConfig {
  command: DroneCommand
  glyph: string
  label: string
  position: string
  variant?: 'direction' | 'stop'
}

// Cross layout: forward on top, stop in the middle, backward at the bottom.
const PAD_BUTTONS: PadButtonConfig[] = [
  { command: 'forward', glyph: '▲', label: 'FORWARD', position: 'col-start-2 row-start-1' },
  { command: 'left', glyph: '◀', label: 'LEFT', position: 'col-start-1 row-start-2' },
  {
    command: 'stop',
    glyph: '■',
    label: 'STOP',
    position: 'col-start-2 row-start-2',
    variant: 'stop',
  },
  { command: 'right', glyph: '▶', label: 'RIGHT', position: 'col-start-3 row-start-2' },
  { command: 'backward', glyph: '▼', label: 'BACKWARD', position: 'col-start-2 row-start-3' },
]

export function DroneControls({
  connected,
  requestedCommand,
  speed,
  onPress,
  onRelease,
  onSpeedChange,
}: DroneControlsProps) {
  return (
    <Panel title="DRONE CONTROLS">
      <div className="mx-auto grid w-full max-w-sm grid-cols-3 gap-3">
        {PAD_BUTTONS.map(({ command: buttonCommand, glyph, label, position, variant }) => (
          <DirectionButton
            key={buttonCommand}
            command={buttonCommand}
            glyph={glyph}
            label={label}
            variant={variant}
            className={position}
            active={requestedCommand === buttonCommand}
            disabled={!connected && buttonCommand !== 'stop'}
            onPress={onPress}
            onRelease={onRelease}
          />
        ))}
      </div>

      <div className="mt-5">
        <SpeedControl value={speed} disabled={!connected} onChange={onSpeedChange} />
      </div>

      {/* Secondary hint: the keyboard is an extra input method, never a
          replacement for the buttons above. */}
      <p className="mt-4 text-center font-mono text-[10px] tracking-[0.16em] text-ink-mute">
        KEYBOARD — W / A / S / D OR ARROW KEYS MOVE · SPACE STOP
      </p>
    </Panel>
  )
}
