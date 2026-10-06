import type { DroneService } from './droneService'
import type {
  BatteryStatus,
  CameraMode,
  CameraStatus,
  CommandAck,
  ConnectionStatus,
  DroneCommand,
  DroneStatus,
  ServiceStatus,
} from '../types/drone'
import { normalizeBatteryStatus, normalizeDroneStatus } from '../utils/status'

// The ONLY module in the frontend that performs HTTP. UI components depend on
// the DroneService abstraction, never on fetch (specs/integration/frontend-api-service.md).

/** Backend wire shape (specs/backend/drone-api.md): flat, lowercase enums. */
interface WireStatus {
  state?: {
    connection?: ConnectionStatus
    camera?: CameraStatus
    requestedCommand?: DroneCommand
    confirmedCommand?: DroneCommand
    speed?: number
  }
  raspberryPi?: ConnectionStatus
  api?: ConnectionStatus
}

/** Minimal response surface used by the adapter; keeps injected fakes simple. */
interface FetchResponseLike {
  ok: boolean
  status: number
  text(): Promise<string>
}

type FetchLike = (url: string, init?: { method?: string; body?: string; headers?: Record<string, string> }) => Promise<FetchResponseLike>

const CONNECTION_SERVICES: readonly string[] = ['offline', 'connecting', 'online', 'error']

// Wire → UI derivation (mapping table in the spec): connected/streaming are
// the "serving" states; offline/connecting/error pass through by name;
// anything unknown fails safe to 'offline'.
function toServiceStatus(value: string | undefined): ServiceStatus {
  if (value === 'connected' || value === 'streaming') {
    return 'online'
  }
  return value !== undefined && CONNECTION_SERVICES.includes(value)
    ? (value as ServiceStatus)
    : 'offline'
}

function toUiStatus(raw: unknown): DroneStatus {
  const wire = (raw ?? {}) as WireStatus
  const state = wire.state ?? {}
  return normalizeDroneStatus({
    connection: state.connection,
    camera: state.camera,
    services: {
      drone: toServiceStatus(state.connection),
      raspberryPi: toServiceStatus(wire.raspberryPi),
      camera: toServiceStatus(state.camera),
      api: toServiceStatus(wire.api),
    },
    requestedCommand: state.requestedCommand,
    confirmedCommand: state.confirmedCommand,
    // The wire has no failed-command concept: command failures arrive as HTTP
    // rejections, which the dashboard hook turns into a status reload.
    failedCommand: null,
    speed: state.speed,
    // normalizeDroneStatus replaces every missing field with its rest-state
    // default, so partial wire objects can never reach the UI un-normalized.
  } as DroneStatus)
}

function describeProblem(payload: unknown): string {
  if (payload !== null && typeof payload === 'object') {
    const { title, detail } = payload as { title?: unknown; detail?: unknown }
    const parts = [title, detail].filter((p): p is string => typeof p === 'string' && p.length > 0)
    if (parts.length > 0) {
      return `: ${parts.join(' — ')}`
    }
  }
  return ''
}

export function createApiDroneService(
  baseUrl: string,
  fetchImpl: FetchLike = (input, init) => globalThis.fetch(input, init),
): DroneService {
  const base = baseUrl.replace(/\/+$/, '')

  async function request(
    method: 'GET' | 'POST' | 'PUT',
    path: string,
    body?: unknown,
  ): Promise<unknown> {
    const url = `${base}${path}`
    const response = await fetchImpl(url, {
      method,
      ...(body === undefined
        ? {}
        : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
    })
    const text = await response.text()
    let payload: unknown
    if (text.length > 0) {
      try {
        payload = JSON.parse(text)
      } catch {
        payload = undefined
      }
    }
    if (!response.ok) {
      // Rejections always name the status code; callers never see a silent error.
      throw new Error(`API ${method} ${path} failed with HTTP ${response.status}${describeProblem(payload)}`)
    }
    if (payload === undefined) {
      throw new Error(`API ${method} ${path} returned an unreadable success body`)
    }
    return payload
  }

  return {
    getStatus: async () => toUiStatus(await request('GET', '/api/drone/status')),

    connect: async () => toUiStatus(await request('POST', '/api/drone/connect')),

    disconnect: async () => toUiStatus(await request('POST', '/api/drone/disconnect')),

    sendCommand: async (command: DroneCommand): Promise<CommandAck> => {
      await request('POST', '/api/drone/command', { command })
      // Truthful label only: the backend confirms through its simulator today,
      // and the wire carries no acknowledgement-source field. No path here can
      // ever claim a hardware confirmation (AGENTS safety rule).
      return { command, source: 'simulated', confirmedByHardware: false }
    },

    setSpeed: async (speed: number) => toUiStatus(await request('PUT', '/api/drone/speed', { speed })),

    // Battery is a separate additive control-plane endpoint
    // (specs/hardware/battery-monitoring.md); the frozen DroneStatus wire is
    // untouched. A partial/unexpected body is normalized at the seam so the UI
    // can only ever see a valid BatteryStatus.
    getBattery: async (): Promise<BatteryStatus> =>
      normalizeBatteryStatus((await request('GET', '/api/battery')) as BatteryStatus),

    // Camera mode is NOT part of the drone wire contract. It is a same-origin
    // capability document served by the frontend nginx (Task 33), so the API
    // service fetches it directly — never via the backend base URL, and still
    // confined to this module (the only place the frontend calls fetch).
    // 404 (vite dev, mock deployment without the file) => mock; the explicit
    // ustreamer/mock body values are the only accepted payloads; anything else
    // rejects so a deployment can never silently degrade to the simulated UI.
    getCameraMode: async (): Promise<CameraMode> => {
      const response = await fetchImpl('/camera-mode.json', { method: 'GET' })
      if (response.status === 404) {
        return 'mock'
      }
      const text = await response.text()
      let payload: unknown
      if (text.length > 0) {
        try {
          payload = JSON.parse(text)
        } catch {
          payload = undefined
        }
      }
      if (!response.ok) {
        throw new Error(`Camera mode request failed with HTTP ${response.status}`)
      }
      const mode = (payload as { mode?: unknown } | undefined)?.mode
      if (mode === 'ustreamer' || mode === 'mock') {
        return mode
      }
      throw new Error('Camera mode document is missing or invalid')
    },
  }
}
