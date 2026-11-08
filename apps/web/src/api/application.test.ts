import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { clientsForPerson } from './application'
import {
  authenticationMode,
  initializeSession,
  selectedPersonId,
  sessionExpired,
} from '../state/session'

const identity = 'opaque-test-identity'
beforeEach(async () => {
  vi.stubGlobal('localStorage', { removeItem: vi.fn() })
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          mode: 'oidc',
          authenticated: true,
          user: { id: identity, displayName: 'Konto' },
          csrfToken: 'test-csrf',
        }),
      ),
    ),
  )
  await initializeSession(new AbortController().signal)
})
afterEach(() => vi.unstubAllGlobals())

it('sends session-bound CSRF metadata with same-origin credentials and no demo identity', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
  vi.stubGlobal('fetch', fetch)
  await expect(clientsForPerson(identity).trips.listMyTrips({})).rejects.toThrow()
  expect(fetch).toHaveBeenCalledOnce()
  const init = fetch.mock.calls[0][1]
  const headers = new Headers(init.headers)
  expect(headers.get('X-CSRF-Token')).toBe('test-csrf')
  expect(headers.has('X-Carpoolr-Demo-User')).toBe(false)
  expect(headers.has('Authorization')).toBe(false)
  expect(init).toMatchObject({ credentials: 'same-origin', cache: 'no-store' })
  expect(sessionExpired.value).toBe(true)
  expect(selectedPersonId.value).toBe(identity)
})

it('blocks a stale identity before making a network request', async () => {
  const client = clientsForPerson(identity)
  selectedPersonId.value = 'another-identity'
  const fetch = vi.fn()
  vi.stubGlobal('fetch', fetch)
  await expect(client.trips.listMyTrips({})).rejects.toThrow('geändert')
  expect(fetch).not.toHaveBeenCalled()
})

it('recognizes a changed cookie session without replaying the rejected mutation', async () => {
  const fetch = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ error: 'invalid_csrf_token' }), {
      status: 403,
      headers: { 'content-type': 'application/json' },
    }),
  )
  vi.stubGlobal('fetch', fetch)
  await expect(
    clientsForPerson(identity).bookings.requestSeat({ tripId: 'trip' }),
  ).rejects.toThrow()
  expect(fetch).toHaveBeenCalledOnce()
  expect(sessionExpired.value).toBe(true)
  expect(selectedPersonId.value).toBe(identity)
})

it('does not confuse a business permission failure with an expired session', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'forbidden' }), {
        status: 403,
        headers: { 'content-type': 'application/json' },
      }),
    ),
  )
  await expect(clientsForPerson(identity).trips.listMyTrips({})).rejects.toThrow()
  expect(sessionExpired.value).toBe(false)
})

it('keeps explicit demo metadata in demo mode and does not send a CSRF token', async () => {
  authenticationMode.value = 'demo'
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
  vi.stubGlobal('fetch', fetch)
  await expect(clientsForPerson(identity).trips.listMyTrips({})).rejects.toThrow()
  const headers = new Headers(fetch.mock.calls[0][1].headers)
  expect(headers.get('X-Carpoolr-Demo-User')).toBe(identity)
  expect(headers.has('X-CSRF-Token')).toBe(false)
  expect(sessionExpired.value).toBe(false)
})
