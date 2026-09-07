import { describe, expect, it } from 'vitest'
import { health } from './client'

// Integration test (NOT hermetic): the BFF must be running first.
//   dotnet run --project apps/api   (or: make dev-api)
// Run with: bun run test:integration
// Exercises the real client module, including VITE_API_BASE_URL resolution.
describe('api client integration', () => {
  it('health() returns ok from the live BFF', async () => {
    const result = await health()
    expect(result.status).toBe('ok')
  })
})
