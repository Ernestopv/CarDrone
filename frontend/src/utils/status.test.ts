import { describe, expect, it } from 'vitest'
import type { DroneStatus } from '../types/drone'
import {
  cameraView,
  clampSpeed,
  commandLabel,
  connectionView,
  createApiUnavailableStatus,
  createCameraFailureStatus,
  createConnectingDroneStatus,
  createErrorDroneStatus,
  createInitialDroneStatus,
  createResetDroneStatus,
  createSystemServices,
  normalizeDroneStatus,
  serviceView,
} from './status'

describe('createInitialDroneStatus', () => {
  it('describes a fully offline drone', () => {
    expect(createInitialDroneStatus()).toEqual({
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

describe('normalizeDroneStatus', () => {
  it('fills every missing field with a safe default', () => {
    const stale = { ...createInitialDroneStatus() } as Partial<DroneStatus>
    delete stale.connection
    delete stale.requestedCommand
    delete stale.failedCommand
    delete stale.speed

    const normalized = normalizeDroneStatus(stale as DroneStatus)
    expect(normalized.connection).toBe('offline')
    expect(normalized.camera).toBe('offline')
    expect(normalized.requestedCommand).toBe('stop')
    expect(normalized.confirmedCommand).toBe('stop')
    expect(normalized.failedCommand).toBeNull()
    expect(normalized.speed).toBe(0)
    expect(normalized.services).toEqual(createSystemServices('offline'))
  })

  it('passes a complete status through unchanged', () => {
    const status: DroneStatus = {
      ...createInitialDroneStatus(),
      connection: 'connected',
      camera: 'streaming',
      requestedCommand: 'forward',
      confirmedCommand: 'forward',
      speed: 45,
    }
    expect(normalizeDroneStatus(status)).toEqual(status)
  })
})

describe('clampSpeed', () => {
  it('clamps below zero and above one hundred', () => {
    expect(clampSpeed(-10)).toBe(0)
    expect(clampSpeed(150)).toBe(100)
  })

  it('rounds and keeps in-range values', () => {
    expect(clampSpeed(42.4)).toBe(42)
    expect(clampSpeed(45)).toBe(45)
    expect(clampSpeed(0)).toBe(0)
    expect(clampSpeed(100)).toBe(100)
  })
})

describe('label helpers', () => {
  it('labels commands in upper case', () => {
    expect(commandLabel('stop')).toBe('STOP')
    expect(commandLabel('forward')).toBe('FORWARD')
    expect(commandLabel('backward')).toBe('BACKWARD')
    expect(commandLabel('left')).toBe('LEFT')
    expect(commandLabel('right')).toBe('RIGHT')
  })

  it('maps every connection state to a label and tone', () => {
    expect(connectionView('offline')).toEqual({ label: 'DISCONNECTED', tone: 'muted' })
    expect(connectionView('connecting')).toEqual({ label: 'CONNECTING', tone: 'warning' })
    expect(connectionView('connected')).toEqual({ label: 'CONNECTED', tone: 'success' })
    expect(connectionView('error')).toEqual({ label: 'ERROR', tone: 'danger' })
  })

  it('maps every service state to a label and tone', () => {
    expect(serviceView('offline')).toEqual({ label: 'Offline', tone: 'muted' })
    expect(serviceView('connecting')).toEqual({ label: 'Connecting', tone: 'warning' })
    expect(serviceView('online')).toEqual({ label: 'Online', tone: 'success' })
    expect(serviceView('error')).toEqual({ label: 'Error', tone: 'danger' })
  })

  it('maps every camera state to a label and tone', () => {
    expect(cameraView('offline')).toEqual({ label: 'OFFLINE', tone: 'muted' })
    expect(cameraView('connecting')).toEqual({ label: 'CONNECTING', tone: 'warning' })
    expect(cameraView('streaming')).toEqual({ label: 'STREAMING', tone: 'success' })
    expect(cameraView('error')).toEqual({ label: 'ERROR', tone: 'danger' })
  })
})

describe('state factories', () => {
  it('connecting moves drone, camera and all services to connecting together', () => {
    const status = createConnectingDroneStatus(createInitialDroneStatus())
    expect(status.connection).toBe('connecting')
    expect(status.camera).toBe('connecting')
    expect(status.services).toEqual(createSystemServices('connecting'))
    expect(status.requestedCommand).toBe('stop')
  })

  it('error takes the drone and camera down together', () => {
    const base: DroneStatus = {
      ...createInitialDroneStatus(),
      connection: 'connected',
      camera: 'streaming',
      services: createSystemServices('online'),
      speed: 60,
    }
    const status = createErrorDroneStatus(base)
    expect(status.connection).toBe('error')
    expect(status.camera).toBe('error')
    expect(status.services.drone).toBe('error')
    expect(status.services.camera).toBe('error')
    expect(status.services.api).toBe('online')
    expect(status.services.raspberryPi).toBe('online')
    expect(status.speed).toBe(60)
  })

  it('api-unavailable fails only the API service', () => {
    const base: DroneStatus = {
      ...createInitialDroneStatus(),
      connection: 'connected',
      camera: 'streaming',
      services: createSystemServices('online'),
    }
    const status = createApiUnavailableStatus(base)
    expect(status.services.api).toBe('error')
    expect(status.services.drone).toBe('online')
    expect(status.services.camera).toBe('online')
    expect(status.connection).toBe('connected')
    expect(status.camera).toBe('streaming')
  })

  it('camera-failure fails only the camera', () => {
    const base: DroneStatus = {
      ...createInitialDroneStatus(),
      connection: 'connected',
      camera: 'streaming',
      services: createSystemServices('online'),
    }
    const status = createCameraFailureStatus(base)
    expect(status.camera).toBe('error')
    expect(status.services.camera).toBe('error')
    expect(status.connection).toBe('connected')
    expect(status.services.api).toBe('online')
  })

  it('reset from error returns to a healthy connected state', () => {
    const status = createResetDroneStatus({
      ...createInitialDroneStatus(),
      connection: 'error',
      camera: 'error',
      services: { ...createSystemServices('online'), drone: 'error', camera: 'error' },
      requestedCommand: 'forward',
      confirmedCommand: 'forward',
      failedCommand: 'forward',
      speed: 60,
    })
    expect(status.connection).toBe('connected')
    expect(status.camera).toBe('streaming')
    expect(status.services).toEqual(createSystemServices('online'))
    expect(status.requestedCommand).toBe('stop')
    expect(status.confirmedCommand).toBe('stop')
    expect(status.failedCommand).toBeNull()
    expect(status.speed).toBe(60)
  })

  it('reset never fabricates a connection: offline stays offline', () => {
    const status = createResetDroneStatus({
      ...createInitialDroneStatus(),
      services: createSystemServices('online'),
    })
    expect(status.connection).toBe('offline')
    expect(status.camera).toBe('offline')
    expect(status.services).toEqual(createSystemServices('offline'))
  })
})
