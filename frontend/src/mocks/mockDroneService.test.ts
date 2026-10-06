import { describe, expect, it } from 'vitest'
import { createInitialDroneStatus } from '../utils/status'
import { createMockDroneService } from './mockDroneService'

describe('createMockDroneService', () => {
  it('starts fully offline', async () => {
    const service = createMockDroneService()
    await expect(service.getStatus()).resolves.toEqual(createInitialDroneStatus())
  })

  it('reports the mock camera mode (Task 33)', async () => {
    const service = createMockDroneService()
    expect(await service.getCameraMode()).toBe('mock')
  })

  it('reports a deterministic simulated battery (Task 41)', async () => {
    const service = createMockDroneService()
    await expect(service.getBattery()).resolves.toEqual({
      available: true,
      voltage: 7.8,
      percent: 64,
      state: 'ok',
      simulated: true,
      current: -0.45,
      power: -3.51,
    })
  })

  it('connects to a healthy simulated state', async () => {
    const service = createMockDroneService()
    const status = await service.connect()
    expect(status).toMatchObject({
      connection: 'connected',
      camera: 'streaming',
      services: { drone: 'online', raspberryPi: 'online', camera: 'online', api: 'online' },
      requestedCommand: 'stop',
      confirmedCommand: 'stop',
      failedCommand: null,
      speed: 0,
    })
  })

  it('disconnects back to the full offline status', async () => {
    const service = createMockDroneService()
    await service.connect()
    await expect(service.disconnect()).resolves.toEqual(createInitialDroneStatus())
  })

  it('records nothing while offline', async () => {
    const service = createMockDroneService()
    const ack = await service.sendCommand('forward')
    expect(ack).toEqual({ command: 'forward', source: 'simulated', confirmedByHardware: false })
    await expect(service.getStatus()).resolves.toMatchObject({
      requestedCommand: 'stop',
      confirmedCommand: 'stop',
      failedCommand: null,
    })
  })

  it('requests before confirming and acks only as simulated', async () => {
    const service = createMockDroneService()
    await service.connect()

    const pending = service.sendCommand('forward')
    // The request is visible immediately; confirmation waits for the ack.
    await expect(service.getStatus()).resolves.toMatchObject({
      requestedCommand: 'forward',
      confirmedCommand: 'stop',
    })

    const ack = await pending
    expect(ack).toEqual({ command: 'forward', source: 'simulated', confirmedByHardware: false })
    await expect(service.getStatus()).resolves.toMatchObject({
      requestedCommand: 'forward',
      confirmedCommand: 'forward',
      failedCommand: null,
    })
  })

  it('clamps speed while connected', async () => {
    const service = createMockDroneService()
    await service.connect()
    await expect(service.setSpeed(150)).resolves.toMatchObject({ speed: 100 })
    await expect(service.setSpeed(-20)).resolves.toMatchObject({ speed: 0 })
    await expect(service.setSpeed(45)).resolves.toMatchObject({ speed: 45 })
  })

  it('ignores speed changes while offline', async () => {
    const service = createMockDroneService()
    await expect(service.setSpeed(80)).resolves.toMatchObject({ speed: 0 })
  })
})
