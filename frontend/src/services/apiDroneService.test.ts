import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApiDroneService } from './apiDroneService'
import type { DroneStatus } from '../types/drone'

interface RecordedCall {
  url: string
  method: string
  body: string | undefined
}

interface FakeInit {
  method?: string
  body?: string
}

function fakeResponse(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => (body === undefined ? '' : JSON.stringify(body)),
  }
}

function createFetchFake(...responses: Array<ReturnType<typeof fakeResponse>>) {
  const calls: RecordedCall[] = []
  let next = 0
  const impl = vi.fn(async (url: string, init?: FakeInit) => {
    calls.push({ url, method: init?.method ?? 'GET', body: init?.body })
    return responses[next++] ?? responses[responses.length - 1]
  })
  return { impl, calls }
}

const connectedWire = {
  state: {
    connection: 'connected',
    camera: 'streaming',
    requestedCommand: 'forward',
    confirmedCommand: 'forward',
    speed: 45,
  },
  raspberryPi: 'connected',
  api: 'connected',
}

const offlineWire = {
  state: {
    connection: 'offline',
    camera: 'offline',
    requestedCommand: 'stop',
    confirmedCommand: 'stop',
    speed: 0,
  },
  raspberryPi: 'offline',
  api: 'connected',
}

describe('createApiDroneService — request contract', () => {
  it('getStatus issues GET /api/drone/status', async () => {
    const { impl, calls } = createFetchFake(fakeResponse(200, offlineWire))

    await createApiDroneService('http://api.test', impl).getStatus()

    expect(calls).toEqual([{ url: 'http://api.test/api/drone/status', method: 'GET', body: undefined }])
  })

  it('connect and disconnect issue POSTs to their endpoints', async () => {
    const { impl, calls } = createFetchFake(
      fakeResponse(200, connectedWire),
      fakeResponse(200, offlineWire),
    )
    const service = createApiDroneService('http://api.test', impl)

    await service.connect()
    await service.disconnect()

    expect(calls.map((c) => [c.method, c.url])).toEqual([
      ['POST', 'http://api.test/api/drone/connect'],
      ['POST', 'http://api.test/api/drone/disconnect'],
    ])
  })

  it('sendCommand posts the command as JSON', async () => {
    const { impl, calls } = createFetchFake(fakeResponse(200, connectedWire))

    await createApiDroneService('http://api.test', impl).sendCommand('forward')

    expect(calls[0]).toEqual({
      url: 'http://api.test/api/drone/command',
      method: 'POST',
      body: '{"command":"forward"}',
    })
  })

  it('setSpeed puts the speed as JSON', async () => {
    const { impl, calls } = createFetchFake(fakeResponse(200, connectedWire))

    await createApiDroneService('http://api.test', impl).setSpeed(45)

    expect(calls[0]).toEqual({
      url: 'http://api.test/api/drone/speed',
      method: 'PUT',
      body: '{"speed":45}',
    })
  })

  it('trims trailing slashes from the base URL', async () => {
    const { impl, calls } = createFetchFake(fakeResponse(200, offlineWire))

    await createApiDroneService('http://api.test/', impl).getStatus()

    expect(calls[0]?.url).toBe('http://api.test/api/drone/status')
  })
})

describe('createApiDroneService — wire → UI mapping', () => {
  it('maps a connected status into the rich UI shape', async () => {
    const { impl } = createFetchFake(fakeResponse(200, connectedWire))

    const status = await createApiDroneService('http://api.test', impl).getStatus()

    expect(status).toEqual<DroneStatus>({
      connection: 'connected',
      camera: 'streaming',
      services: { drone: 'online', raspberryPi: 'online', camera: 'online', api: 'online' },
      requestedCommand: 'forward',
      confirmedCommand: 'forward',
      failedCommand: null,
      speed: 45,
    })
  })

  it('maps an offline status with the API online', async () => {
    const { impl } = createFetchFake(fakeResponse(200, offlineWire))

    const status = await createApiDroneService('http://api.test', impl).getStatus()

    expect(status.connection).toBe('offline')
    expect(status.camera).toBe('offline')
    expect(status.services).toEqual({
      drone: 'offline',
      raspberryPi: 'offline',
      camera: 'offline',
      api: 'online',
    })
  })

  it('a valid JSON body with an unexpected shape normalizes to rest-state defaults', async () => {
    const { impl } = createFetchFake(fakeResponse(200, {}))

    const status = await createApiDroneService('http://api.test', impl).getStatus()

    expect(status).toEqual<DroneStatus>({
      connection: 'offline',
      camera: 'offline',
      services: { drone: 'offline', raspberryPi: 'offline', camera: 'offline', api: 'offline' },
      requestedCommand: 'stop',
      confirmedCommand: 'stop',
      failedCommand: null,
      speed: 0,
    })
  })
})

describe('createApiDroneService — acknowledgements and failures', () => {
  it('sendCommand resolves the honest simulated acknowledgement', async () => {
    const { impl } = createFetchFake(fakeResponse(200, connectedWire))

    const ack = await createApiDroneService('http://api.test', impl).sendCommand('left')

    expect(ack).toEqual({ command: 'left', source: 'simulated', confirmedByHardware: false })
  })

  it('rejects a 409 (command while disconnected) naming the status and problem text', async () => {
    const { impl } = createFetchFake(
      fakeResponse(409, {
        title: 'The drone is not connected',
        detail: 'The drone is not connected.',
        status: 409,
      }),
    )

    await expect(
      createApiDroneService('http://api.test', impl).sendCommand('forward'),
    ).rejects.toThrow(/409/)
  })

  it('rejects a 500 including the ProblemDetails detail', async () => {
    const { impl } = createFetchFake(
      fakeResponse(500, { status: 500, title: 'Unexpected error', detail: 'generic server failure' }),
    )

    await expect(createApiDroneService('http://api.test', impl).getStatus()).rejects.toThrow(
      /HTTP 500.*generic server failure/,
    )
  })

  it('propagates network failures as rejections', async () => {
    const impl = vi.fn(async () => {
      throw new TypeError('Failed to fetch')
    })

    await expect(createApiDroneService('http://api.test', impl).getStatus()).rejects.toThrow(
      'Failed to fetch',
    )
  })

  it('rejects a 200 whose body is not readable JSON', async () => {
    const impl = vi.fn(async () => ({
      ok: true,
      status: 200,
      text: async () => 'not json at all',
    }))

    await expect(createApiDroneService('http://api.test', impl).getStatus()).rejects.toThrow(
      /unreadable success body/,
    )
  })
})

describe('droneService selection (environment seam)', () => {
  beforeEach(() => {
    vi.resetModules()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('defaults to the mock: exports behave as the shared mock instance', async () => {
    const { droneService, scenarioService } = await import('./droneService')

    // Mock-mode proof: scenarios work and share state with the service.
    const afterReset = await scenarioService.runScenario('reset')
    expect(afterReset.connection).toBe('offline')
    expect((await droneService.getStatus()).connection).toBe('offline')
  })

  it("in 'api' mode the scenario guard rejects explicitly", async () => {
    vi.stubEnv('VITE_DRONE_SERVICE', 'api')
    vi.stubEnv('VITE_API_BASE_URL', 'http://api.invalid')
    const { droneService, scenarioService } = await import('./droneService')

    // API-mode proof: the guard rejects, and the service is fetch-backed
    // ('.invalid' can never resolve, so the call must reject over the wire).
    await expect(scenarioService.runScenario('reset')).rejects.toThrow(/API-backed/)
    await expect(droneService.getStatus()).rejects.toBeDefined()
  })
})

describe('createApiDroneService — camera mode (Task 33)', () => {
  it('reads the same-origin capability document', async () => {
    const { impl, calls } = createFetchFake(fakeResponse(200, { mode: 'ustreamer' }))
    const service = createApiDroneService('http://api.test', impl)

    await expect(service.getCameraMode()).resolves.toBe('ustreamer')
    expect(calls[0].url).toBe('/camera-mode.json')
    expect(calls[0].method).toBe('GET')
  })

  it('accepts an explicit mock document', async () => {
    const { impl } = createFetchFake(fakeResponse(200, { mode: 'mock' }))
    const service = createApiDroneService('http://api.test', impl)

    await expect(service.getCameraMode()).resolves.toBe('mock')
  })

  it('maps an absent document (404) to mock', async () => {
    const { impl } = createFetchFake(fakeResponse(404, undefined))
    const service = createApiDroneService('http://api.test', impl)

    await expect(service.getCameraMode()).resolves.toBe('mock')
  })

  it('rejects a non-404 HTTP failure', async () => {
    const { impl } = createFetchFake(fakeResponse(500, undefined))
    const service = createApiDroneService('http://api.test', impl)

    await expect(service.getCameraMode()).rejects.toThrow(/HTTP 500/)
  })

  it('rejects a 200 body without a recognizable camera mode', async () => {
    const { impl } = createFetchFake(fakeResponse(200, { state: { connection: 'offline' } }))
    const service = createApiDroneService('http://api.test', impl)

    await expect(service.getCameraMode()).rejects.toThrow(/missing or invalid/)
  })
})

describe('createApiDroneService — battery (Task 41)', () => {
  it('issues GET /api/battery and maps the reading', async () => {
    const { impl, calls } = createFetchFake(
      fakeResponse(200, {
        available: true,
        voltage: 6.668,
        percent: 28,
        state: 'ok',
        simulated: false,
        current: -0.7457,
        power: -4.9723,
      }),
    )

    const battery = await createApiDroneService('http://api.test', impl).getBattery()

    expect(calls).toEqual([{ url: 'http://api.test/api/battery', method: 'GET', body: undefined }])
    expect(battery).toEqual({
      available: true,
      voltage: 6.668,
      percent: 28,
      state: 'ok',
      simulated: false,
      current: -0.7457,
      power: -4.9723,
    })
  })

  it('normalizes a partial wire body to a valid BatteryStatus', async () => {
    const { impl } = createFetchFake(fakeResponse(200, {}))

    const battery = await createApiDroneService('http://api.test', impl).getBattery()

    expect(battery).toEqual({
      available: false,
      voltage: null,
      percent: null,
      state: 'unknown',
      simulated: false,
      current: null,
      power: null,
    })
  })

  it('propagates HTTP failures as rejections', async () => {
    const { impl } = createFetchFake(fakeResponse(503, { status: 503, title: 'unavailable' }))

    await expect(createApiDroneService('http://api.test', impl).getBattery()).rejects.toThrow(
      /503.*unavailable/,
    )
  })
})
