/// <reference types="vite/client" />

// Environment surface for the service selection seam
// (specs/integration/frontend-api-service.md). Both variables are optional;
// droneService.ts documents their defaults.
interface ImportMetaEnv {
  readonly VITE_DRONE_SERVICE?: 'mock' | 'api' | string
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
