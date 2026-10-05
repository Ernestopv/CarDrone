import { defineConfig } from 'vitest/config'

// Kept separate from vite.config.ts on purpose: tests need a DOM environment
// and the RTL cleanup hook, but neither the Tailwind nor the React Fast
// Refresh plugins (esbuild applies the tsconfig JSX transform by itself).
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
  },
})
