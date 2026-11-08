import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { currentProfile, loadPeople, people, selectedPersonId, selectPerson } from './session'

const storageKey = 'carpoolr:selected-demo-person'
const first = { id: 'server-person-one', displayName: 'Erste Testperson' }
const second = { id: 'server-person-two', displayName: 'Zweite Testperson' }
let storage: Map<string, string>

beforeEach(() => {
  storage = new Map()
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
    removeItem: (key: string) => storage.delete(key),
  })
  people.value = []
  selectedPersonId.value = ''
  currentProfile.value = undefined
})

afterEach(() => vi.unstubAllGlobals())

function catalog(body: unknown = { enabled: true, users: [first, second] }) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify(body), {
        headers: { 'content-type': 'application/json' },
      }),
    ),
  )
}

describe('explicit demo person selection', () => {
  it('loads server people without automatically selecting the first person', async () => {
    catalog()
    await loadPeople(new AbortController().signal)
    expect(people.value).toEqual([first, second])
    expect(selectedPersonId.value).toBe('')
  })

  it('restores a previous selection only if the server still offers it', async () => {
    storage.set(storageKey, second.id)
    catalog()
    await loadPeople(new AbortController().signal)
    expect(selectedPersonId.value).toBe(second.id)
  })

  it('discards an unknown persisted identity instead of impersonating a default person', async () => {
    storage.set(storageKey, 'removed-user')
    catalog()
    await loadPeople(new AbortController().signal)
    expect(selectedPersonId.value).toBe('')
    expect(storage.has(storageKey)).toBe(false)
  })

  it('rejects disabled or malformed catalogs', async () => {
    for (const body of [
      { enabled: false, users: [] },
      { enabled: true, users: [{ id: 1 }] },
    ]) {
      catalog(body)
      await expect(loadPeople(new AbortController().signal)).rejects.toThrow('nicht verfügbar')
    }
    expect(selectedPersonId.value).toBe('')
  })

  it('permits selecting only identities present in the catalog', async () => {
    catalog()
    await loadPeople(new AbortController().signal)
    selectPerson(first.id)
    expect(storage.get(storageKey)).toBe(first.id)
    selectPerson('forged-id')
    expect(selectedPersonId.value).toBe('')
    expect(storage.has(storageKey)).toBe(false)
  })
})
