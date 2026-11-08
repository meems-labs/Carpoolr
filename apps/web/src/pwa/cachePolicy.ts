// Deliberately enumerate the app shell. Personal data and API responses do not belong here.
const fixedAssets = new Set([
  '/index.html',
  '/manifest.webmanifest',
  '/icons/icon.svg',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/icon-maskable-512.png',
  '/icons/apple-touch-icon.png',
])

export function isStaticShellAsset(url: URL, origin: string): boolean {
  return (
    url.origin === origin &&
    !url.search &&
    (fixedAssets.has(url.pathname) || /^\/assets\/.+\.(?:js|css|woff2?)$/.test(url.pathname))
  )
}

export const navigationAllowlist = [
  /^\/(?:\?.*)?$/,
  /^\/(?:profil|meine-fahrten|meine-anfragen|eingegangene-anfragen|kosten)\/?(?:\?.*)?$/,
  /^\/fahrten\/(?:neu|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/?(?:\?.*)?$/i,
]

export const navigationDenylist = [
  /^\/(?:api|auth|oauth|oidc|login|logout|signin|signout|callback|demo|healthz|readyz)(?:\/|\?|$)/i,
  /^\/carpoolr\./i,
  /[?&](?:code|state|access_token|id_token|session_state|error)=/i,
]

const authenticationQueryKeys = new Set([
  'code',
  'state',
  'access_token',
  'id_token',
  'session_state',
  'error',
])

export function isAppShellNavigation(url: URL, origin: string): boolean {
  const path = url.pathname + url.search
  let hasAuthenticationQuery = false
  // Decode parameter names before checking them, including callbacks such as ?%63ode=.
  url.searchParams.forEach((_value, key) => {
    if (authenticationQueryKeys.has(key.toLowerCase())) hasAuthenticationQuery = true
  })
  return (
    url.origin === origin &&
    !hasAuthenticationQuery &&
    navigationAllowlist.some((pattern) => pattern.test(path)) &&
    !navigationDenylist.some((pattern) => pattern.test(path))
  )
}
