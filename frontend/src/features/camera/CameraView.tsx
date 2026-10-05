import { Panel } from '../../components/ui/Panel'
import { CAMERA_STREAM_PATH, useCameraStream } from './useCameraStream'
import type { CameraMode, CameraStatus, DroneCommand } from '../../types/drone'
import { commandLabel } from '../../utils/status'

interface CameraViewProps {
  status: CameraStatus
  /** The OSD shows the command the service has acknowledged. */
  confirmedCommand: DroneCommand
  speed: number
  /**
   * Runtime camera deployment mode (Task 33): 'mock' keeps the simulated
   * presentation; 'ustreamer' renders the real MJPEG feed; null while the
   * mode is still being resolved; cameraUnavailable when resolution failed —
   * a failed resolution must never fall back to the simulated UI.
   */
  cameraMode: CameraMode | null
  cameraUnavailable?: boolean
}

interface CameraMessage {
  label: string
  text: string
  toneClass: string
  hint: string
}

// Mock mode: the camera area stays the Phase 1 placeholder. The simulator owns
// every camera state transition. Presentation only.
function simulatedMessage(status: CameraStatus): CameraMessage {
  switch (status) {
    case 'offline':
      return {
        label: 'CAMERA',
        text: 'NO SIGNAL',
        toneClass: 'text-ink-dim',
        hint: 'Connect the drone to start the simulated feed.',
      }
    case 'connecting':
      return {
        label: 'CAMERA',
        text: 'CONNECTING...',
        toneClass: 'text-caution',
        hint: 'Waiting for the camera…',
      }
    case 'streaming':
      return {
        label: 'CAMERA ACTIVE',
        text: 'SIMULATED STREAM',
        toneClass: 'text-ok',
        hint: 'Simulated video — no hardware is attached.',
      }
    case 'error':
      return {
        label: 'CAMERA',
        text: 'STREAM ERROR',
        toneClass: 'text-warn',
        hint: 'Video unavailable. Reconnect the drone and try again.',
      }
    default:
      // Runtime safety: an unexpected state must never be presented as a
      // successful stream.
      return {
        label: 'CAMERA',
        text: 'STREAM ERROR',
        toneClass: 'text-warn',
        hint: 'Unexpected camera state. Reconnect the drone and try again.',
      }
  }
}

// Ustreamer (real) mode: status-driven, honest overlays. The simulated labels
// never appear here; LIVE FEED only after a first frame rendered.
function liveMessage(status: CameraStatus, phase: string): CameraMessage {
  switch (status) {
    case 'offline':
      return {
        label: 'CAMERA',
        text: 'NO SIGNAL',
        toneClass: 'text-ink-dim',
        hint: 'Connect the drone to start the video feed.',
      }
    case 'connecting':
      return {
        label: 'CAMERA',
        text: 'CONNECTING...',
        toneClass: 'text-caution',
        hint: 'Waiting for the camera…',
      }
    case 'streaming':
      if (phase === 'live') {
        return {
          label: 'CAMERA ACTIVE',
          text: 'LIVE FEED',
          toneClass: 'text-ok',
          hint: 'Live video — the stream is rendering. No physical verification is implied.',
        }
      }
      if (phase === 'error') {
        return {
          label: 'CAMERA',
          text: 'STREAM ERROR',
          toneClass: 'text-warn',
          hint: 'Video lost. Reconnecting…',
        }
      }
      return {
        label: 'CAMERA',
        text: 'CONNECTING...',
        toneClass: 'text-caution',
        hint: 'Waiting for the video feed…',
      }
    case 'error':
      return {
        label: 'CAMERA',
        text: 'STREAM ERROR',
        toneClass: 'text-warn',
        hint: 'Video unavailable. Reconnect the drone and try again.',
      }
    default:
      return {
        label: 'CAMERA',
        text: 'STREAM ERROR',
        toneClass: 'text-warn',
        hint: 'Unexpected camera state. Reconnect the drone and try again.',
      }
  }
}

const UNRESOLVED_MESSAGE: CameraMessage = {
  label: 'CAMERA',
  text: 'CHECKING…',
  toneClass: 'text-ink-dim',
  hint: 'Determining the camera mode…',
}

const UNAVAILABLE_MESSAGE: CameraMessage = {
  label: 'CAMERA',
  text: 'CAMERA UNAVAILABLE',
  toneClass: 'text-warn',
  hint: 'The camera mode could not be determined — no simulated video is shown.',
}

export function CameraView({
  status,
  confirmedCommand,
  speed,
  cameraMode,
  cameraUnavailable = false,
}: CameraViewProps) {
  const realMode = cameraMode === 'ustreamer'
  // Hooks must run unconditionally. `enabled` becomes true only in real mode
  // while the status says streaming.
  const { phase, frameVersion, onFrame, onFeedError, onFeedMount } = useCameraStream(
    realMode && status === 'streaming',
  )

  const message =
    cameraUnavailable
      ? UNAVAILABLE_MESSAGE
      : realMode
        ? liveMessage(status, phase)
        : cameraMode === null
          ? UNRESOLVED_MESSAGE
          : simulatedMessage(status)

  // Lightweight loading cue; the state text itself is always present, so
  // animation is never the only indication.
  const pulseClass = message.text === 'CONNECTING...' || message.text === 'CHECKING…' ? ' animate-pulse' : ''

  return (
    <Panel title="CAMERA VIEW">
      <div className="relative aspect-video w-full overflow-hidden border border-rule bg-[#04070c]">
        {/* Real MJPEG feed (ustreamer mode, status streaming). Decorative: all
            status information lives in the overlay text below. */}
        {realMode && status === 'streaming' && (
          <img
            key={frameVersion}
            ref={onFeedMount}
            src={CAMERA_STREAM_PATH}
            alt=""
            aria-hidden="true"
            onLoad={onFrame}
            onError={onFeedError}
            className="absolute inset-0 h-full w-full object-cover"
          />
        )}

        {/* Viewfinder corner marks */}
        <span
          aria-hidden="true"
          className="absolute top-2 left-2 h-4 w-4 border-t border-l border-hud/40"
        />
        <span
          aria-hidden="true"
          className="absolute top-2 right-2 h-4 w-4 border-t border-r border-hud/40"
        />
        <span
          aria-hidden="true"
          className="absolute bottom-10 left-2 h-4 w-4 border-b border-l border-hud/40"
        />
        <span
          aria-hidden="true"
          className="absolute right-2 bottom-10 h-4 w-4 border-b border-r border-hud/40"
        />

        <div className="absolute inset-0 flex flex-col items-center justify-center px-6 pb-8 text-center">
          <p className="font-mono text-[11px] tracking-[0.3em] text-ink-mute">{message.label}</p>
          <p
            role="status"
            className={`mt-2 font-mono text-xl font-semibold tracking-[0.2em] sm:text-2xl ${message.toneClass}${pulseClass}`}
          >
            {message.text}
          </p>
          <p className="mt-3 max-w-[42ch] text-[13px] leading-relaxed text-ink-mute">
            {message.hint}
          </p>
        </div>

        {/* OSD bar: the live readouts a pilot watches over the feed. */}
        <div className="absolute inset-x-0 bottom-0 flex items-center justify-between gap-4 border-t border-rule/70 bg-night/75 px-3 py-2">
          <span className="font-mono text-[11px] tracking-[0.14em] text-ink-mute">
            CMD{' '}
            <span className="font-semibold text-hud">{commandLabel(confirmedCommand)}</span>
          </span>
          <span className="font-mono text-[11px] tracking-[0.14em] text-ink-mute">
            SPD <span className="font-semibold text-ink">{speed}%</span>
          </span>
        </div>
      </div>
    </Panel>
  )
}