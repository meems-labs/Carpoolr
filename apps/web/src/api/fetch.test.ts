import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { apiReachable } from '../state/connection'
import { fetchApi } from './fetch'

beforeEach(async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}')))
  await fetchApi('https://api.carpoolr.test/ready')
})
afterEach(() => vi.unstubAllGlobals())

it('reports network failure even when browser online status is true, then recovers on a response', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
  await expect(fetchApi('https://api.carpoolr.test/profile')).rejects.toThrow()
  expect(apiReachable.value).toBe(false)
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}')))
  await fetchApi('https://api.carpoolr.test/profile')
  expect(apiReachable.value).toBe(true)
})

it('does not treat cancelled navigation requests as a network outage', async () => {
  const controller = new AbortController()
  controller.abort()
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new DOMException('Aborted', 'AbortError')))
  await expect(
    fetchApi('https://api.carpoolr.test/profile', { signal: controller.signal }),
  ).rejects.toThrow()
  expect(apiReachable.value).toBe(true)
})

it('keeps HTTP authorization errors separate from connection failures', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))
  const response = await fetchApi('https://api.carpoolr.test/profile')
  expect(response.status).toBe(401)
  expect(apiReachable.value).toBe(true)
})
