// API-mode hook integration (specs/integration/frontend-backend.md):
// selection pinned to 'api' with a stubbed fetch so the hook exercises the
// REAL adapter (droneService → createApiDroneService → fetch) without network.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import type { DroneStatus } from '../types/drone'

interface FakeInit {
  method?: string
  body?: string
}

function fakeResponse(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => JSON.stringify(body),
  }
}

const connectedStatus: DroneStatus = {
  connection: 'connected',
  camera: 'streaming',
  services: { drone: 'online', raspberryPi: 'online', camera: 'online', api: 'online' },
  requestedCommand: 'stop',
  confirmedCommand: 'stop',
  failedCommand: null,
  speed: 30,
}

const offlineStatus: DroneStatus = {
  connection: 'offline',
  camera: 'offline',
  services: { drone: 'offline', raspberryPi: 'offline', camera: 'offline', api: 'online' },
  requestedCommand: 'stop',
  confirmedCommand: 'stop',
  failedCommand: null,
  speed: 0,
}

function wire(status: DroneStatus) {
  // The backend's flat wire shape (specs/backend/drone-api.md).
  return {
    state: {
      connection: status.connection,
      camera: status.camera,
      requestedCommand: status.requestedCommand,
      confirmedCommand: status.confirmedCommand,
      speed: status.speed,
    },
    raspberryPi: status.services.raspberryPi === 'online' ? 'connected' : 'offline',
    api: 'connected',
  }
}

async function renderApiModeHook(
  fetchImpl: (url: string, init?: FakeInit) => Promise<ReturnType<typeof fakeResponse>>,
) {
  vi.stubGlobal('fetch', vi.fn(fetchImpl))
  const mod = await import('./useDroneDashboard')
  const view = renderHook(() => mod.useDroneDashboard())
  return { result: view.result, view }
}

beforeEach(() => {
  vi.resetModules()
  vi.stubEnv('VITE_DRONE_SERVICE', 'api')
  vi.stubEnv('VITE_API_BASE_URL', 'http://api.test')
})

afterEach(() => {
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('useDroneDashboard in api mode', () => {
  it('replaces the placeholder status with the backend truth on mount', async () => {
    const { result } = await renderApiModeHook(async () => fakeResponse(200, wire(connectedStatus)))

    await waitFor(() => expect(result.current.status.connection).toBe('connected'))
    expect(result.current.status.services.drone).toBe('online')
    expect(result.current.status.speed).toBe(30)
  })

  it('surfaces an unreachable backend as the existing error state on mount', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { result } = await renderApiModeHook(async () => {
      throw new TypeError('Failed to fetch')
    })

    await waitFor(() => expect(result.current.status.connection).toBe('error'))
    expect(errorSpy).toHaveBeenCalled()
  })

  it('keeps acknowledgement semantics: confirmed moves only after the service resolves', async () => {
    let state = connectedStatus
    let releaseCommand: (() => void) | undefined
    const { result } = await renderApiModeHook(async (_url: string, init?: FakeInit) => {
      if (init?.method === 'POST' && init.body) {
        if (!releaseCommand) {
          await new Promise<void>((resolve) => {
            releaseCommand = resolve
          })
        }
        const command = (JSON.parse(init.body) as { command: DroneStatus['requestedCommand'] })
          .command
        state = { ...state, requestedCommand: command, confirmedCommand: command }
      }
      return fakeResponse(200, wire(state))
    })
    await waitFor(() => expect(result.current.status.connection).toBe('connected'))

    let promise!: Promise<void>
    act(() => {
      promise = result.current.sendCommand('forward')
    })

    // While the request is in flight the confirmation must not have moved.
    await waitFor(() => expect(result.current.status.requestedCommand).toBe('forward'))
    expect(result.current.status.confirmedCommand).toBe('stop')

    releaseCommand?.()
    await act(async () => {
      await promise
    })
    expect(result.current.status.confirmedCommand).toBe('forward')
  })

  it('converges on the backend truth when a command is rejected (409)', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    let commandCount = 0
    const { result } = await renderApiModeHook(async (_url: string, init?: FakeInit) => {
      if (init?.method === 'POST' && init.body) {
        commandCount += 1
        // The backend rejects: the drone is actually offline now.
        return fakeResponse(409, { title: 'The drone is not connected', detail: 'no link' })
      }
      return fakeResponse(200, wire(offlineStatus))
    })
    await waitFor(() => expect(result.current.status.connection).toBe('offline'))

    await act(async () => {
      await result.current.sendCommand('stop') // STOP is always sent, even offline
    })

    expect(commandCount).toBe(1)
    // The rejection was logged and status reloaded: offline, no fabricated confirmation.
    expect(errorSpy).toHaveBeenCalledWith(
      'Drone service rejected the command:',
      expect.objectContaining({ message: expect.stringContaining('409') }),
    )
    expect(result.current.status.connection).toBe('offline')
    expect(result.current.status.confirmedCommand).toBe('stop')
  })

  it('runScenario is a silent no-op in api mode (guard beyond the hidden panel)', async () => {
    const { result } = await renderApiModeHook(async () => fakeResponse(200, wire(offlineStatus)))
    await waitFor(() => expect(result.current.status.services.api).toBe('online'))

    await act(async () => {
      await result.current.runScenario('connectionLost')
    })

    // No error state, no scenario damage: the mock service was never touched.
    expect(result.current.status.connection).toBe('offline')
  })
})
