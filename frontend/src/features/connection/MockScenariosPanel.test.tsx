import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { MockScenario } from '../../services/scenarioService'
import { MockScenariosPanel } from './MockScenariosPanel'

const TRIGGERS: Array<{ label: string; scenario: MockScenario }> = [
  { label: 'CONNECTION LOST', scenario: 'connectionLost' },
  { label: 'API UNAVAILABLE', scenario: 'apiUnavailable' },
  { label: 'CAMERA FAILURE', scenario: 'cameraFailure' },
  { label: 'COMMAND FAILURE', scenario: 'commandFailure' },
  { label: 'RESET', scenario: 'reset' },
]

describe('MockScenariosPanel', () => {
  it('offers five triggers, visibly marked as simulated', () => {
    render(<MockScenariosPanel disabled={false} onRunScenario={vi.fn()} />)
    expect(screen.getAllByRole('button')).toHaveLength(5)
    expect(screen.getByText('SIMULATED')).toBeTruthy()
    expect(screen.getByText(/PHASE 1 TEST TRIGGERS/)).toBeTruthy()
    expect(screen.getByText(/STOP never fails/)).toBeTruthy()
  })

  it('fires the callback with the matching scenario', () => {
    const onRunScenario = vi.fn()
    render(<MockScenariosPanel disabled={false} onRunScenario={onRunScenario} />)
    for (const { label, scenario } of TRIGGERS) {
      fireEvent.click(screen.getByRole('button', { name: label }))
      expect(onRunScenario).toHaveBeenCalledWith(scenario)
    }
    expect(onRunScenario).toHaveBeenCalledTimes(5)
  })

  it('disables every trigger while a connection attempt is pending', () => {
    render(<MockScenariosPanel disabled onRunScenario={vi.fn()} />)
    for (const button of screen.getAllByRole('button')) {
      expect(button).toHaveProperty('disabled', true)
    }
  })
})
