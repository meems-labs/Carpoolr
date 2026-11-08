import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  authenticationMode,
  currentProfile,
  initializeSession,
  loginUrl,
  logout,
  markSessionExpired,
  people,
  selectedPersonId,
  selectPerson,
  sessionExpired,
  sessionHeaders,
  signedInUser,
} from './session'

const user = { id: 'opaque-server-identity', displayName: 'Kollegin' }
const signal = () => new AbortController().signal
const session = (overrides: Record<string, unknown> = {}) => ({
  mode: 'oidc',
  authenticated: true,
  user,
  csrfToken: 'request-csrf',
  ...overrides,
})
function respond(body: unknown, status = 200) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status }))
  vi.stubGlobal('fetch', fetch)
  return fetch
}

beforeEach(() => {
  authenticationMode.value = 'unknown'
  selectedPersonId.value = ''
  signedInUser.value = null
  currentProfile.value = undefined
  people.value = []
  sessionExpired.value = false
  vi.stubGlobal('localStorage', { getItem: () => null, setItem: vi.fn(), removeItem: vi.fn() })
})
afterEach(() => vi.unstubAllGlobals())

describe('server-owned browser session', () => {
  it('discovers the mode without storing login credentials or loading a demo catalog', async () => {
    const fetch = respond(session())
    await initializeSession(signal())
    expect(fetch).toHaveBeenCalledOnce()
    expect(fetch.mock.calls[0][0]).toMatch(/\/auth\/session$/)
    expect(fetch.mock.calls[0][1]).toMatchObject({ cache: 'no-store', credentials: 'same-origin' })
    expect(authenticationMode.value).toBe('oidc')
    expect(selectedPersonId.value).toBe(user.id)
    expect(signedInUser.value).toEqual(user)
    expect(localStorage.setItem).not.toHaveBeenCalled()
    expect(sessionHeaders(user.id)).toEqual({ 'X-CSRF-Token': 'request-csrf' })
  })

  it('ignores a persisted demo identity for an anonymous OIDC visitor', async () => {
    vi.stubGlobal('localStorage', { getItem: () => 'demo-id', removeItem: vi.fn() })
    respond(session({ authenticated: false, user: null, csrfToken: null }))
    await initializeSession(signal())
    expect(selectedPersonId.value).toBe('')
    expect(people.value).toEqual([])
    selectPerson('demo-id')
    expect(selectedPersonId.value).toBe('')
  })

  it('loads a demo catalog only after the server explicitly enables that mode', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify(
            session({ mode: 'demo', authenticated: false, user: null, csrfToken: null }),
          ),
        ),
      )
      .mockResolvedValueOnce(new Response(JSON.stringify({ enabled: true, users: [user] })))
    vi.stubGlobal('fetch', fetch)
    await initializeSession(signal())
    expect(fetch).toHaveBeenCalledTimes(2)
    expect(authenticationMode.value).toBe('demo')
    selectPerson(user.id)
    expect(sessionHeaders(user.id)).toEqual({ 'X-Carpoolr-Demo-User': user.id })
  })

  it.each([404, 503])(
    'never falls back to a demo identity after session endpoint HTTP %s',
    async (status) => {
      const fetch = respond({}, status)
      await expect(initializeSession(signal())).rejects.toThrow('Anmeldung')
      expect(fetch).toHaveBeenCalledOnce()
      expect(selectedPersonId.value).toBe('')
    },
  )

  it.each([
    session({ mode: 'unexpected' }),
    session({ user: null }),
    session({ csrfToken: null }),
    session({ user: { id: '', displayName: 'Name' } }),
    session({ authenticated: false }),
  ])('rejects malformed authenticated state', async (body) => {
    respond(body)
    await expect(initializeSession(signal())).rejects.toThrow('ungültige Anmeldekonfiguration')
    expect(selectedPersonId.value).toBe('')
  })

  it('rejects cross-origin cookie sign-in configuration', async () => {
    vi.stubGlobal('window', { location: { origin: 'https://carpoolr.test' } })
    respond(session())
    await expect(initializeSession(signal())).rejects.toThrow('derselben Adresse')
    expect(selectedPersonId.value).toBe('')
  })

  it('does not submit from an old view after the active identity changes', async () => {
    respond(session())
    await initializeSession(signal())
    selectedPersonId.value = 'other-identity'
    expect(() => sessionHeaders(user.id)).toThrow('geändert')
  })

  it('preserves visible state on expiry and blocks subsequent submissions', async () => {
    respond(session())
    await initializeSession(signal())
    const profile = { displayName: 'Ungespeicherter Kontext' } as NonNullable<
      typeof currentProfile.value
    >
    currentProfile.value = profile
    markSessionExpired()
    expect(sessionExpired.value).toBe(true)
    expect(selectedPersonId.value).toBe(user.id)
    expect(currentProfile.value?.displayName).toBe(profile.displayName)
    expect(() => sessionHeaders(user.id)).toThrow('abgelaufen')
  })

  it('clears personal data only after confirmed server logout', async () => {
    respond(session())
    await initializeSession(signal())
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetch)
    await logout(signal())
    expect(fetch.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-Token': 'request-csrf' },
    })
    expect(selectedPersonId.value).toBe('')
    expect(currentProfile.value).toBeUndefined()
    expect(signedInUser.value).toBeNull()
    expect(() => sessionHeaders(user.id)).toThrow()
  })

  it('keeps the current session if the logout request fails', async () => {
    respond(session())
    await initializeSession(signal())
    respond({}, 503)
    await expect(logout(signal())).rejects.toThrow('Abmeldung')
    expect(selectedPersonId.value).toBe(user.id)
    expect(sessionHeaders(user.id)).toHaveProperty('X-CSRF-Token')
  })

  it.each(['https://evil.test/', '//evil.test/', '/\\evil.test', '/\nredirect'])(
    'drops unsafe login return destination %s',
    async (destination) => {
      respond(session())
      await initializeSession(signal())
      expect(loginUrl(destination)).toMatch(/returnUrl=%2F$/)
    },
  )

  it('encodes the intended local route for the server-side login', async () => {
    respond(session())
    await initializeSession(signal())
    expect(loginUrl('/kosten?offset=20')).toMatch(/returnUrl=%2Fkosten%3Foffset%3D20$/)
  })
})
