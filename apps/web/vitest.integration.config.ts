import { defineConfig } from 'vitest/config'

// Integration test config: runs api/client.test.ts against the live BFF.
// Prerequisite: the API must be running (dotnet run --project apps/api).
// No vue plugin needed here — the test imports only plain TS modules.
export default defineConfig({
  test: {
    include: ['src/api/**/*.test.ts'],
    testTimeout: 5000,
  },
})
