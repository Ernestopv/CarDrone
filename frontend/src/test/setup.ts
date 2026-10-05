import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// React's act() refuses to run unless the environment opts in; without test
// globals React Testing Library cannot set this itself.
const actEnvironment = globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }
actEnvironment.IS_REACT_ACT_ENVIRONMENT = true

// Task 19 made the API implementation the default; every existing suite is a
// behavior test of the abstraction against the SIMULATOR. Pin mock mode here
// (before any test module imports the selection seam); API-mode suites stub
// the variable explicitly with vi.stubEnv and restore it themselves.
;(import.meta.env as { VITE_DRONE_SERVICE?: string }).VITE_DRONE_SERVICE = 'mock'

// React Testing Library only auto-registers cleanup when the test framework
// exposes globals, so the setup file does it explicitly.
afterEach(cleanup)
