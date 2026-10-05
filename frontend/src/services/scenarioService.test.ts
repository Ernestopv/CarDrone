import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { DroneService } from './droneService'
import { droneService, scenarioService } from './droneService'

// These tests share the application's service singletons, so every test
// starts from the same known point: fully offline.
beforeEach(async () => {
  await droneService.disconnect()
})

afterEach(async () => {
  await droneService.disconnect()
})

describe('scenario: connectionLost', () => {
  it('drops drone and camera into error together', async () => {
    await droneService.connect()
    const status = await scenarioService.runScenario('connectionLost')
    expect(status.connection).toBe('error')
    expect(status.camera).toBe('error')
    expect(status.services.drone).toBe('error')
    expect(status.services.camera).toBe('error')
  })

  it('never confirms commands while lost', async () => {
    await droneService.connect()
    await scenarioService.runScenario('connectionLost')

    const ack = await droneService.sendCommand('forward')
    expect(ack.confirmedByHardware).toBe(false)
    expect(ack.source).toBe('simulated')
    const status = await droneService.getStatus()
    expect(status.confirmedCommand).not.toBe('forward')
  })

  it('reconnecting clears the failure', async () => {
    await droneService.connect()
    await scenarioService.runScenario('connectionLost')

    const status = await droneService.connect()
    expect(status.connection).toBe('connected')
    expect(status.camera).toBe('streaming')
    expect(status.services).toEqual({
      drone: 'online',
      raspberryPi: 'online',
      camera: 'online',
      api: 'online',
    })
    expect(status.failedCommand).toBeNull()
  })
})

describe('scenario: apiUnavailable', () => {
  it('errors only the API service', async () => {
    await droneService.connect()
    const status = await scenarioService.runScenario('apiUnavailable')
    expect(status.services.api).toBe('error')
    expect(status.connection).toBe('connected')
    expect(status.camera).toBe('streaming')
    expect(status.services.drone).toBe('online')
  })

  it('keeps commands working during the outage', async () => {
    await droneService.connect()
    await scenarioService.runScenario('apiUnavailable')
    await droneService.sendCommand('right')
    expect((await droneService.getStatus()).confirmedCommand).toBe('right')
  })

  it('reset restores the API', async () => {
    await droneService.connect()
    await scenarioService.runScenario('apiUnavailable')
    const status = await scenarioService.runScenario('reset')
    expect(status.services.api).toBe('online')
    expect(status.connection).toBe('connected')
  })
})

describe('scenario: cameraFailure', () => {
  it('errors the camera while the drone stays connected', async () => {
    await droneService.connect()
    const status = await scenarioService.runScenario('cameraFailure')
    expect(status.camera).toBe('error')
    expect(status.services.camera).toBe('error')
    expect(status.connection).toBe('connected')
    expect(status.services.api).toBe('online')
  })

  it('keeps commands working', async () => {
    await droneService.connect()
    await scenarioService.runScenario('cameraFailure')
    await droneService.sendCommand('left')
    expect((await droneService.getStatus()).confirmedCommand).toBe('left')
  })

  it('reset restores streaming', async () => {
    await droneService.connect()
    await scenarioService.runScenario('cameraFailure')
    const status = await scenarioService.runScenario('reset')
    expect(status.camera).toBe('streaming')
    expect(status.services.camera).toBe('online')
  })
})

describe('scenario: commandFailure', () => {
  it('rejects the next movement command without confirming it', async () => {
    await droneService.connect()
    await scenarioService.runScenario('commandFailure')

    await expect(droneService.sendCommand('forward')).rejects.toThrow(/rejected/)

    const status = await droneService.getStatus()
    expect(status.failedCommand).toBe('forward')
    expect(status.requestedCommand).toBe('forward')
    expect(status.confirmedCommand).toBe('stop')
    expect(status.connection).toBe('connected')
  })

  it('never fails STOP, does not consume the arm, and is one-shot', async () => {
    await droneService.connect()
    await scenarioService.runScenario('commandFailure')

    // STOP succeeds while armed — the rest state must stay reachable.
    const stopAck = await droneService.sendCommand('stop')
    expect(stopAck).toEqual({ command: 'stop', source: 'simulated', confirmedByHardware: false })

    // The arm is still set: the next movement command is rejected…
    await expect(droneService.sendCommand('left')).rejects.toThrow()
    // …and the one after that succeeds and clears the recorded failure.
    await droneService.sendCommand('right')

    const status = await droneService.getStatus()
    expect(status.failedCommand).toBeNull()
    expect(status.confirmedCommand).toBe('right')
    expect(status.connection).toBe('connected')
  })
})

describe('scenario: reset', () => {
  it('restores a healthy state and preserves speed', async () => {
    await droneService.connect()
    await droneService.setSpeed(45)
    await scenarioService.runScenario('connectionLost')

    const status = await scenarioService.runScenario('reset')
    expect(status.connection).toBe('connected')
    expect(status.camera).toBe('streaming')
    expect(status.services).toEqual({
      drone: 'online',
      raspberryPi: 'online',
      camera: 'online',
      api: 'online',
    })
    expect(status.requestedCommand).toBe('stop')
    expect(status.confirmedCommand).toBe('stop')
    expect(status.failedCommand).toBeNull()
    expect(status.speed).toBe(45)
  })

  it('never fabricates a connection: offline stays offline', async () => {
    const status = await scenarioService.runScenario('reset')
    expect(status.connection).toBe('offline')
    expect(status.camera).toBe('offline')
  })
})

describe('DroneService contract', () => {
  it('does not expose scenario triggers (compile-time)', () => {
    // @ts-expect-error scenario triggers must stay outside the DroneService
    // contract — a future API implementation has no failure simulations.
    const forbidden: keyof DroneService = 'runScenario'
    expect(forbidden).toBe('runScenario')
  })
})
