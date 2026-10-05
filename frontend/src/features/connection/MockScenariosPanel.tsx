import { Panel } from '../../components/ui/Panel'
import type { MockScenario } from '../../services/scenarioService'

interface MockScenariosPanelProps {
  /** Disabled while a connection attempt is pending. */
  disabled: boolean
  onRunScenario: (scenario: MockScenario) => void
}

interface ScenarioButtonConfig {
  scenario: MockScenario
  label: string
}

const SCENARIO_BUTTONS: ScenarioButtonConfig[] = [
  { scenario: 'connectionLost', label: 'CONNECTION LOST' },
  { scenario: 'apiUnavailable', label: 'API UNAVAILABLE' },
  { scenario: 'cameraFailure', label: 'CAMERA FAILURE' },
  { scenario: 'commandFailure', label: 'COMMAND FAILURE' },
  { scenario: 'reset', label: 'RESET' },
]

const BUTTON_CLASSES =
  'rounded-[3px] border border-rule bg-night/40 px-2 py-2 font-mono text-[10px] font-semibold tracking-[0.1em] text-ink-dim transition-colors hover:border-hud/50 hover:text-hud disabled:cursor-not-allowed disabled:opacity-40'

// Phase 1 test triggers only. The panel is clearly marked as simulated so a
// simulated failure is never mistaken for a real one, and every button goes
// through the dashboard hook — nothing mutates state from here.
export function MockScenariosPanel({ disabled, onRunScenario }: MockScenariosPanelProps) {
  return (
    <Panel title="MOCK SCENARIOS">
      <div className="flex flex-wrap items-center gap-2">
        <span className="border border-caution/40 bg-caution/10 px-1.5 py-0.5 font-mono text-[10px] font-semibold tracking-[0.14em] text-caution">
          SIMULATED
        </span>
        <span className="font-mono text-[10px] tracking-[0.1em] text-ink-mute">
          PHASE 1 TEST TRIGGERS
        </span>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2">
        {SCENARIO_BUTTONS.map(({ scenario, label }) => (
          <button
            key={scenario}
            type="button"
            disabled={disabled}
            onClick={() => onRunScenario(scenario)}
            className={`${BUTTON_CLASSES} ${scenario === 'reset' ? 'col-span-2 border-hud/40 text-hud' : ''}`}
          >
            {label}
          </button>
        ))}
      </div>

      <p className="mt-3 text-[11px] leading-snug text-ink-mute">
        “Command failure” rejects the next movement command — STOP never fails. “Reset” restores
        healthy state without changing your speed.
      </p>
    </Panel>
  )
}
