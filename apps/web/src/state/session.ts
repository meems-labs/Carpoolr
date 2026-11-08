import { ref } from 'vue'
import { apiBaseUrl } from '../api/config'
import { fetchApi } from '../api/fetch'
import type { UserProfile } from '../api/generated/trips/v1/trips_pb'

export interface DemoPerson {
  id: string
  displayName: string
}

export type AuthenticationMode = 'unknown' | 'demo' | 'oidc' | 'bearer'

interface SessionResponse {
  mode: Exclude<AuthenticationMode, 'unknown'>
  authenticated: boolean
  user: DemoPerson | null
  csrfToken: string | null
}

const storageKey = 'carpoolr:selected-demo-person'
export const people = ref<DemoPerson[]>([])
export const selectedPersonId = ref('')
export const currentProfile = ref<UserProfile>()
export const authenticationMode = ref<AuthenticationMode>('unknown')
export const signedInUser = ref<DemoPerson | null>(null)
// Keep a stale session's forms visible until the user explicitly signs in again.
export const sessionExpired = ref(false)
let csrfToken: string | null = null

export async function initializeSession(signal: AbortSignal): Promise<void> {
  const response = await fetchApi(`${apiBaseUrl.replace(/\/$/, '')}/auth/session`, {
    signal,
    cache: 'no-store',
    credentials: 'same-origin',
  })
  if (!response.ok)
    throw new Error('Die Anmeldung konnte nicht geprüft werden. Bitte erneut versuchen.')
  const data: unknown = await response.json()
  signal.throwIfAborted()
  if (!isSession(data))
    throw new Error('Der Server hat eine ungültige Anmeldekonfiguration geliefert.')
  if (
    data.mode === 'oidc' &&
    typeof window !== 'undefined' &&
    new URL(apiBaseUrl, window.location.origin).origin !== window.location.origin
  )
    throw new Error(
      'Für die Anmeldung muss die API unter derselben Adresse wie die App erreichbar sein.',
    )

  authenticationMode.value = data.mode
  sessionExpired.value = false
  csrfToken = data.csrfToken
  signedInUser.value = data.user
  if (data.mode === 'demo') {
    await loadPeople(signal)
  } else {
    people.value = []
    currentProfile.value = undefined
    selectedPersonId.value = data.authenticated ? data.user!.id : ''
    // A demo selection is never a login credential or a real-user profile mapping.
    try {
      localStorage.removeItem(storageKey)
    } catch {
      /* Storage is optional. */
    }
  }
}

/** Metadata is captured for a specific identity; stale views cannot submit as another person. */
export function sessionHeaders(personId: string): HeadersInit {
  if (!personId || personId !== selectedPersonId.value)
    throw new Error('Die aktive Person hat sich geändert. Bitte die Ansicht erneut öffnen.')
  if (authenticationMode.value === 'demo') return { 'X-Carpoolr-Demo-User': personId }
  if (authenticationMode.value !== 'oidc' || !csrfToken || sessionExpired.value)
    throw new Error('Deine Sitzung ist abgelaufen. Bitte erneut anmelden.')
  return { 'X-CSRF-Token': csrfToken }
}

export function markSessionExpired(): void {
  if (authenticationMode.value === 'oidc') sessionExpired.value = true
}

export function loginUrl(returnUrl: string): string {
  if (authenticationMode.value !== 'oidc')
    throw new Error('Die Anmeldung ist noch nicht eingerichtet.')
  // The backend validates this again. Never accept an external or protocol-relative destination.
  const safeReturn =
    /^\/(?!\/)/.test(returnUrl) && !/[\\\u0000-\u001f\u007f]/.test(returnUrl) ? returnUrl : '/'
  return `${apiBaseUrl.replace(/\/$/, '')}/auth/login?returnUrl=${encodeURIComponent(safeReturn)}`
}

export async function logout(signal: AbortSignal): Promise<void> {
  if (authenticationMode.value !== 'oidc' || !csrfToken)
    throw new Error('Es besteht keine angemeldete Sitzung.')
  const response = await fetchApi(`${apiBaseUrl.replace(/\/$/, '')}/auth/logout`, {
    method: 'POST',
    signal,
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { 'X-CSRF-Token': csrfToken },
  })
  signal.throwIfAborted()
  if (!response.ok) throw new Error('Die Abmeldung ist fehlgeschlagen. Bitte erneut versuchen.')
  csrfToken = null
  signedInUser.value = null
  selectedPersonId.value = ''
  currentProfile.value = undefined
  sessionExpired.value = false
}

export async function loadPeople(signal: AbortSignal): Promise<void> {
  const response = await fetchApi(`${apiBaseUrl.replace(/\/$/, '')}/demo/users`, { signal })
  if (!response.ok)
    throw new Error('Die Testpersonen konnten nicht geladen werden. Bitte erneut versuchen.')
  const data: unknown = await response.json()
  signal.throwIfAborted()
  if (!isCatalog(data) || !data.enabled)
    throw new Error('Die Personenauswahl ist auf diesem Server nicht verfügbar.')
  people.value = data.users
  authenticationMode.value = 'demo'
  let saved: string | null = null
  try {
    saved = localStorage.getItem(storageKey)
  } catch {
    /* Selection still works without storage. */
  }
  if (saved && people.value.some((person) => person.id === saved)) selectPerson(saved)
  else selectPerson('')
}

export function selectPerson(id: string): void {
  if (authenticationMode.value !== 'demo') return
  const valid = people.value.some((person) => person.id === id) ? id : ''
  selectedPersonId.value = valid
  currentProfile.value = undefined
  try {
    if (valid) localStorage.setItem(storageKey, valid)
    else localStorage.removeItem(storageKey)
  } catch {
    /* Persistence is optional. */
  }
}

function isPerson(value: unknown): value is DemoPerson {
  return (
    !!value &&
    typeof value === 'object' &&
    'id' in value &&
    typeof value.id === 'string' &&
    value.id.length > 0 &&
    'displayName' in value &&
    typeof value.displayName === 'string'
  )
}

function isSession(value: unknown): value is SessionResponse {
  if (
    !value ||
    typeof value !== 'object' ||
    !('mode' in value) ||
    !['demo', 'oidc', 'bearer'].includes(String(value.mode)) ||
    !('authenticated' in value) ||
    typeof value.authenticated !== 'boolean' ||
    !('user' in value) ||
    !('csrfToken' in value)
  )
    return false
  if (value.authenticated && !isPerson(value.user)) return false
  if (!value.authenticated && value.user !== null) return false
  if (value.mode === 'oidc' && value.authenticated)
    return typeof value.csrfToken === 'string' && value.csrfToken.length > 0
  return value.csrfToken === null || typeof value.csrfToken === 'string'
}

function isCatalog(value: unknown): value is { enabled: boolean; users: DemoPerson[] } {
  if (!value || typeof value !== 'object' || !('enabled' in value) || !('users' in value))
    return false
  return (
    typeof value.enabled === 'boolean' &&
    Array.isArray(value.users) &&
    value.users.every(
      (person: unknown) =>
        !!person &&
        typeof person === 'object' &&
        'id' in person &&
        typeof person.id === 'string' &&
        'displayName' in person &&
        typeof person.displayName === 'string',
    )
  )
}
