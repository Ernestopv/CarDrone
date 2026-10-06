import { useEffect, useState } from 'react'
import { droneService } from '../services/droneService'
import type { BatteryStatus } from '../types/drone'
import {
  createErrorBatteryStatus,
  createInitialBatteryStatus,
  normalizeBatteryStatus,
} from '../utils/status'

/** Poll cadence (battery changes slowly; a fixed software constant). */
const BATTERY_POLL_MS = 5000

export interface UseBatteryMonitorResult {
  status: BatteryStatus
}

/**
 * Battery telemetry polling. State arrives through the DroneService
 * abstraction (never over a raw fetch); a rejected call surfaces as the honest
 * error state instead of throwing into a render. The timer is cleared on
 * unmount so no leak survives the component.
 */
export function useBatteryMonitor(): UseBatteryMonitorResult {
  // Lazy initializer: React calls it once on mount and no object is shared.
  const [status, setStatus] = useState<BatteryStatus>(createInitialBatteryStatus)

  useEffect(() => {
    let active = true

    const load = async () => {
      try {
        const next = await droneService.getBattery()
        if (active) setStatus(normalizeBatteryStatus(next))
      } catch (error) {
        // Never swallow the cause: log it, then surface the error state.
        console.error('Battery service call failed:', error)
        if (active) setStatus(createErrorBatteryStatus())
      }
    }

    load()
    const interval = window.setInterval(load, BATTERY_POLL_MS)
    return () => {
      active = false
      window.clearInterval(interval)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return { status }
}