import { describe, expect, it } from 'vitest'
import { isAppShellNavigation, isStaticShellAsset } from './cachePolicy'

const origin = 'https://carpoolr.example'
const url = (path: string) => new URL(path, origin)

describe('static app-shell cache boundary', () => {
  it.each([
    '/index.html',
    '/manifest.webmanifest',
    '/assets/index-123.js',
    '/assets/ProfileView-456.css',
    '/assets/font-123.woff2',
    '/icons/icon-192.png',
    '/icons/icon-maskable-512.png',
  ])('allows the known build asset %s', (path) => {
    expect(isStaticShellAsset(url(path), origin)).toBe(true)
  })

  it.each([
    '/api/healthz',
    '/api/carpoolr.trips.v1.TripService/GetTrip',
    '/demo/users',
    '/auth/callback',
    '/assets/profile.json',
    '/assets/index-123.js.map',
    '/assets/index-123.js?access_token=private',
    '/unknown.js',
    'https://other.example/assets/index-123.js',
  ])('never precaches dynamic, credential-bearing, unknown or cross-origin URL %s', (path) => {
    expect(isStaticShellAsset(url(path), origin)).toBe(false)
  })
})

describe('offline navigation boundary', () => {
  it.each([
    '/',
    '/?from=2026-11-08',
    '/profil',
    '/fahrten',
    '/kosten',
    '/meine-fahrten',
    '/meine-anfragen',
    '/eingegangene-anfragen',
    '/fahrten/neu',
    '/fahrten/11111111-1111-4111-8111-111111111111',
  ])('allows the Vue page %s', (path) => {
    expect(isAppShellNavigation(url(path), origin)).toBe(true)
  })

  it.each([
    '/unknown',
    '/api',
    '/api/healthz',
    '/carpoolr.trips.v1.ProfileService/GetMyProfile',
    '/demo/users',
    '/auth/callback',
    '/kosten?code=private',
    '/?state=private',
    '/?id_token=private',
    '/?%63ode=private',
    '/kosten?%61ccess_token=private',
    '/profil?%53tAtE=private',
    'https://identity.example/profil',
  ])('does not replace API, auth, unknown or cross-origin navigation %s', (path) => {
    expect(isAppShellNavigation(url(path), origin)).toBe(false)
  })
})
