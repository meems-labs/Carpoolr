import { ref } from 'vue'
import { apiBaseUrl } from '../api/application'
import { fetchApi } from '../api/fetch'
import type { UserProfile } from '../api/generated/trips/v1/trips_pb'

export interface DemoPerson {
  id: string
  displayName: string
}

const storageKey = 'carpoolr:selected-demo-person'
export const people = ref<DemoPerson[]>([])
export const selectedPersonId = ref('')
export const currentProfile = ref<UserProfile>()

export async function loadPeople(signal: AbortSignal): Promise<void> {
  const response = await fetchApi(`${apiBaseUrl.replace(/\/$/, '')}/demo/users`, { signal })
  if (!response.ok)
    throw new Error('Die Testpersonen konnten nicht geladen werden. Bitte erneut versuchen.')
  const data: unknown = await response.json()
  if (!isCatalog(data) || !data.enabled)
    throw new Error('Die Personenauswahl ist auf diesem Server nicht verfügbar.')
  people.value = data.users
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
