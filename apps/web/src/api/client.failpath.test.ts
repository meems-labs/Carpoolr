import { afterEach, describe, expect, it, vi } from 'vitest'
import { health } from './client'

// Hermetic failure-path test for the client wrapper (no running services
// required): stubs fetch so /healthz returns a server error, and asserts
// health() rejects with a descriptive error instead of returning a partial
// object. Complements the live integration test in client.test.ts.
describe('api client failure path', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('health() rejects with a descriptive error on a non-OK response', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(null, { status: 503, statusText: 'Service Unavailable' })
      )
    )

    await expect(health()).rejects.toThrow('GET /healthz failed: 503 Service Unavailable')
  })
})