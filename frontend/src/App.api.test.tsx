// API-mode App rendering (specs/integration/frontend-backend.md): the mock
// scenario panel must not be offered when a real backend drives the UI.
// Everything is imported dynamically AFTER the env stub so the selection seam
// evaluates with the intended mode.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'

afterEach(() => {
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe('App in api mode', () => {
  it('hides the mock scenarios panel and reports the flag as unavailable', async () => {
    vi.resetModules()
    vi.stubEnv('VITE_DRONE_SERVICE', 'api')
    vi.stubEnv('VITE_API_BASE_URL', 'http://api.test')

    const { supportsMockScenarios } = await import('./services/droneService')
    expect(supportsMockScenarios).toBe(false)

    const fetchMock = vi.fn(async (url: string) => {
      // Task 33: the api drone service also reads the same-origin capability
      // document; in this API-mode integration here the deployment is mock.
      if (url === '/camera-mode.json') {
        return { ok: true, status: 200, text: async () => JSON.stringify({ mode: 'mock' }) }
      }
      return {
        ok: true,
        status: 200,
        text: async () =>
          JSON.stringify({
            state: {
              connection: 'offline',
              camera: 'offline',
              requestedCommand: 'stop',
              confirmedCommand: 'stop',
              speed: 0,
            },
            raspberryPi: 'offline',
            api: 'connected',
          }),
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const { App } = await import('./App')
    render(<App />)

    // Dashboard renders, scenario tooling does not.
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'SYSTEM STATUS' })).toBeTruthy(),
    )
    expect(screen.queryByRole('heading', { name: 'MOCK SCENARIOS' })).toBeNull()
    expect(fetchMock).toHaveBeenCalled() // mount-load status request really went out
  })
})

describe('App in mock mode (selection opt-out)', () => {
  it('still offers the mock scenarios panel', async () => {
    vi.resetModules()
    vi.stubEnv('VITE_DRONE_SERVICE', 'mock')

    const { supportsMockScenarios } = await import('./services/droneService')
    expect(supportsMockScenarios).toBe(true)
  })
})
