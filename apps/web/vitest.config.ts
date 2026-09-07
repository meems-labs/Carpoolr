import { defineConfig } from 'vitest/config'

// Unit test config (hermetic, no running services required).
// Integration tests live in src/api/client.test.ts and run via
// `bun run test:integration` (vitest.integration.config.ts) instead.
// No vue plugin needed here — unit tests import only plain TS modules.
export default defineConfig({
  test: {
    exclude: ['**/node_modules/**', '**/dist/**', 'src/api/client.test.ts'],
    passWithNoTests: true,
  },
})
