/// <reference lib="webworker" />

import { clientsClaim, setCacheNameDetails } from 'workbox-core'
import {
  cleanupOutdatedCaches,
  createHandlerBoundToURL,
  precacheAndRoute,
  type PrecacheEntry,
} from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'
import {
  isAppShellNavigation,
  isStaticShellAsset,
  navigationAllowlist,
  navigationDenylist,
} from './pwa/cachePolicy'
import { createUpdateGate } from './pwa/workerUpdateGate'

declare const self: ServiceWorkerGlobalScope & {
  readonly __WB_MANIFEST: (PrecacheEntry | string)[]
}

setCacheNameDetails({ prefix: 'carpoolr', suffix: 'app-shell' })
cleanupOutdatedCaches()

// The build manifest is filtered again here so future public assets cannot accidentally
// introduce API, identity, or other private responses into the offline cache.
precacheAndRoute(
  self.__WB_MANIFEST.filter((entry) =>
    isStaticShellAsset(
      new URL(typeof entry === 'string' ? entry : entry.url, self.registration.scope),
      self.location.origin,
    ),
  ),
  { ignoreURLParametersMatching: [], cleanURLs: false },
)

const navigationRoute = new NavigationRoute(createHandlerBoundToURL('/index.html'), {
  allowlist: navigationAllowlist,
  denylist: navigationDenylist,
})
const matchesNavigation = navigationRoute.match
navigationRoute.match = (options) =>
  options.sameOrigin &&
  isAppShellNavigation(options.url, self.location.origin) &&
  matchesNavigation(options)
registerRoute(navigationRoute)

// No API runtime cache, catch-all fetch handler, mutation queue, or background sync.
// Unknown routes and non-GET calls go straight to the network and fail normally offline.
clientsClaim()

const prepareTimeoutMs = 2500

function prepareClient(client: WindowClient): Promise<boolean> {
  return new Promise((resolve) => {
    const channel = new MessageChannel()
    let finished = false
    const finish = (safe: boolean) => {
      if (finished) return
      finished = true
      clearTimeout(timeout)
      channel.port1.onmessage = null
      channel.port1.onmessageerror = null
      channel.port1.close()
      try {
        channel.port2.close()
      } catch {
        // The second port has normally already been transferred to the window.
      }
      resolve(safe)
    }
    const timeout = setTimeout(() => finish(false), prepareTimeoutMs)
    channel.port1.onmessage = (event: MessageEvent<unknown>) => {
      const response = event.data
      finish(
        typeof response === 'object' &&
          response !== null &&
          'safe' in response &&
          response.safe === true,
      )
    }
    channel.port1.onmessageerror = () => finish(false)
    try {
      client.postMessage({ type: 'PWA_UPDATE_PREPARE' }, [channel.port2])
    } catch {
      finish(false)
    }
  })
}

const activateWhenSafe = createUpdateGate<WindowClient>({
  listClients: () => self.clients.matchAll({ type: 'window', includeUncontrolled: true }),
  prepareClient,
  cancelClients: async (clients) => {
    for (const client of clients) {
      try {
        client.postMessage({ type: 'PWA_UPDATE_CANCELLED' })
      } catch {
        // A tab may have closed during the handshake.
      }
    }
  },
  activate: () => self.skipWaiting(),
})

self.addEventListener('message', (event: ExtendableMessageEvent) => {
  if (event.data?.type !== 'ACTIVATE_WHEN_SAFE' || !event.ports[0]) return
  const source = event.source
  if (!source || !('url' in source) || new URL(source.url).origin !== self.location.origin) return
  const reply = event.ports[0]
  event.waitUntil(
    activateWhenSafe((status) => reply.postMessage({ status })).finally(() => reply.close()),
  )
})
