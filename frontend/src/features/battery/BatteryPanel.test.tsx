import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { BatteryStatus } from '../../types/drone'
import { BatteryPanel } from './BatteryPanel'

function statusWith(overrides: Partial<BatteryStatus>): BatteryStatus {
  return {
    available: false,
    voltage: null,
    percent: null,
    state: 'unknown',
    simulated: false,
    current: null,
    power: null,
    ...overrides,
  }
}

describe('BatteryPanel', () => {
  it('renders a healthy reading with voltage, charge, current and power', () => {
    render(
      <BatteryPanel
        status={statusWith({
          available: true,
          voltage: 6.668,
          percent: 28,
          state: 'ok',
          current: -0.7457,
          power: -4.97,
        })}
      />,
    )
    expect(screen.getByText('6.67 V')).toBeTruthy()
    expect(screen.getByText('28%')).toBeTruthy()
    expect(screen.getByText('-0.75 A')).toBeTruthy()
    expect(screen.getByText('-5.0 W')).toBeTruthy()
    expect(screen.getByText('CHARGING')).toBeTruthy()
    expect(screen.getByText('OK')).toBeTruthy()
    expect(screen.queryByText('SIMULATED')).toBeNull()
  })

  it('labels a positive current as discharging', () => {
    render(
      <BatteryPanel
        status={statusWith({
          available: true,
          voltage: 7.2,
          percent: 50,
          state: 'ok',
          current: 1.25,
          power: 9.0,
        })}
      />,
    )
    expect(screen.getByText('1.25 A')).toBeTruthy()
    expect(screen.getByText('DISCHARGING')).toBeTruthy()
  })

  it('shows no direction when the current is zero', () => {
    render(
      <BatteryPanel
        status={statusWith({
          available: true,
          voltage: 7.0,
          percent: 40,
          state: 'ok',
          current: 0,
          power: 0,
        })}
      />,
    )
    expect(screen.getByText('0.00 A')).toBeTruthy()
    expect(screen.queryByText('CHARGING')).toBeNull()
    expect(screen.queryByText('DISCHARGING')).toBeNull()
  })

  it('labels simulated data explicitly', () => {
    render(
      <BatteryPanel status={statusWith({ available: true, voltage: 7.8, percent: 64, state: 'ok', simulated: true })} />,
    )
    expect(screen.getByText('7.80 V')).toBeTruthy()
    expect(screen.getByText('SIMULATED')).toBeTruthy()
  })

  it('renders the low state with its own tone label', () => {
    render(
      <BatteryPanel status={statusWith({ available: true, voltage: 6.4, percent: 17, state: 'low' })} />,
    )
    expect(screen.getByText('LOW')).toBeTruthy()
    expect(screen.getByText('17%')).toBeTruthy()
  })

  it('renders the no-reading state without fabricating values', () => {
    render(<BatteryPanel status={statusWith({})} />)
    expect(screen.getByText('No reading')).toBeTruthy()
    expect(screen.queryByText('%')).toBeNull()
    expect(screen.queryByText('SIMULATED')).toBeNull()
  })

  it('renders the sensor error state', () => {
    render(<BatteryPanel status={statusWith({ state: 'error' })} />)
    expect(screen.getByText('Sensor unavailable')).toBeTruthy()
    expect(screen.getByText('SENSOR ERROR')).toBeTruthy()
  })
})