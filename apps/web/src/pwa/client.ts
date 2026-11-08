import { readonly, ref, watch, type WatchStopHandle } from 'vue'
import { canReloadFromActivity } from '../state/activity'

const available = ref(false)
const applying = ref(false)
const blocked = ref(false)
const error = ref('')
const online = ref(true)
export const updateAvailable = readonly(available)
export const updateApplying = readonly(applying)
export const updateBlocked = readonly(blocked)
export const updateError = readonly(error)
export const isOnline = readonly(online)

const CHECK_INTERVAL = 5 * 60_000
const RETRY_INTERVAL = 10_000
const ACTIVATION_TIMEOUT = 10_000
let registration: ServiceWorkerRegistration | undefined
let started = false
let registrationEnabled = false
let generation = 0
let checking = false
let requestingActivation = false
let composing = false
let reloadRequested = false
let reloadPending = false
let lastCheck = 0
let hadController = false
let releaseTimer: ReturnType<typeof setTimeout> | undefined
let checkTimer: ReturnType<typeof setInterval> | undefined
let retryTimer: ReturnType<typeof setInterval> | undefined
let stopActivityWatch: WatchStopHandle | undefined
const cleanups: Array<() => void> = []
const previousInert = new Map<HTMLElement, boolean>()

function safeToReload(): boolean {
  const element = document.activeElement as HTMLElement | null
  const editing =
    !!element &&
    !element.closest?.('[data-pwa-saved-selection]') &&
    (/^(INPUT|TEXTAREA|SELECT)$/.test(element.tagName) || element.isContentEditable)
  return canReloadFromActivity.value && !composing && !editing
}

function setApplying(value: boolean) {
  applying.value = value
  if (releaseTimer) clearTimeout(releaseTimer)
  releaseTimer = undefined
  if (value) {
    // Lock synchronously before acknowledging readiness to the waiting worker.
    for (const element of document.querySelectorAll<HTMLElement>('[data-pwa-update-lock]')) {
      if (!previousInert.has(element)) previousInert.set(element, element.inert)
      element.inert = true
    }
    releaseTimer = setTimeout(() => setApplying(false), ACTIVATION_TIMEOUT)
  } else {
    for (const [element, inert] of previousInert) element.inert = inert
    previousInert.clear()
  }
}

function captureWaiting() {
  available.value = reloadPending || !!(registration?.waiting && registration.active)
  if (!available.value) blocked.value = false
  if (available.value) void applyUpdate()
}

function observeInstalling(worker: ServiceWorker | null) {
  if (!worker) return
  const changed = () => {
    if (worker.state === 'installed') queueMicrotask(captureWaiting)
  }
  worker.addEventListener('statechange', changed)
  cleanups.push(() => worker.removeEventListener('statechange', changed))
}

export async function checkForUpdates(): Promise<void> {
  if (!started || !registrationEnabled || !online.value || checking || applying.value) return
  checking = true
  lastCheck = Date.now()
  const currentGeneration = generation
  try {
    if (!registration) {
      const result = await navigator.serviceWorker.register(`${import.meta.env.BASE_URL}sw.js`, {
        scope: import.meta.env.BASE_URL,
        updateViaCache: 'none',
      })
      if (currentGeneration !== generation) return
      registration = result
      const found = () => observeInstalling(result.installing)
      result.addEventListener('updatefound', found)
      cleanups.push(() => result.removeEventListener('updatefound', found))
      observeInstalling(result.installing)
      captureWaiting()
    }
    await registration.update()
    if (currentGeneration === generation) {
      error.value = ''
      captureWaiting()
    }
  } catch {
    if (currentGeneration === generation && online.value)
      error.value = 'Die Update-Prüfung ist gerade nicht möglich. Bitte später erneut versuchen.'
  } finally {
    if (currentGeneration === generation) checking = false
  }
}

function requestActivation(worker: ServiceWorker): Promise<string> {
  return new Promise((resolve, reject) => {
    const channel = new MessageChannel()
    const finish = () => {
      clearTimeout(timeout)
      channel.port1.close()
      channel.port2.close()
    }
    const timeout = setTimeout(() => {
      finish()
      reject(new Error('Update worker did not respond.'))
    }, ACTIVATION_TIMEOUT)
    channel.port1.onmessage = (event: MessageEvent<unknown>) => {
      const result = event.data
      if (
        !result ||
        typeof result !== 'object' ||
        !('status' in result) ||
        typeof result.status !== 'string'
      )
        return
      finish()
      resolve(result.status)
    }
    try {
      worker.postMessage({ type: 'ACTIVATE_WHEN_SAFE' }, [channel.port2])
    } catch (cause) {
      finish()
      reject(cause)
    }
  })
}

export async function applyUpdate(): Promise<void> {
  if (started && reloadPending) {
    reloadIfSafe()
    return
  }
  if (!started || !registration?.waiting || !online.value || requestingActivation || applying.value)
    return
  available.value = true
  if (!safeToReload()) {
    blocked.value = true
    return
  }
  requestingActivation = true
  const currentGeneration = generation
  try {
    const status = await requestActivation(registration.waiting)
    if (currentGeneration !== generation) return
    if (status === 'blocked') {
      blocked.value = true
      setApplying(false)
    } else if (status === 'activating') {
      blocked.value = false
      error.value = ''
      setApplying(true)
    } else if (status !== 'checking') throw new Error('Update activation failed.')
  } catch {
    if (currentGeneration === generation) {
      setApplying(false)
      error.value = 'Das Update konnte noch nicht angewendet werden. Die App versucht es erneut.'
    }
  } finally {
    if (currentGeneration === generation) requestingActivation = false
  }
}

function workerMessage(event: MessageEvent) {
  const source = event.source
  if (!source || !('scriptURL' in source) || typeof source.scriptURL !== 'string') return
  const expected = new URL(`${import.meta.env.BASE_URL}sw.js`, window.location.origin)
  if (source.scriptURL !== expected.href) return
  if (event.data?.type === 'PWA_UPDATE_PREPARE') {
    const port = event.ports[0]
    if (!port) return
    const safe = safeToReload()
    available.value = true
    blocked.value = !safe
    if (safe) setApplying(true)
    port.postMessage({ safe })
    port.close()
  } else if (event.data?.type === 'PWA_UPDATE_CANCELLED') {
    setApplying(false)
    blocked.value = true
  }
}

function controllerChanged() {
  const shouldReload = hadController || applying.value
  hadController = true
  if (shouldReload) {
    reloadPending = true
    available.value = true
    reloadIfSafe()
  } else captureWaiting()
}

function reloadIfSafe() {
  if (reloadRequested) return
  if (!safeToReload()) {
    blocked.value = true
    setApplying(false)
    return
  }
  if (reloadPending) {
    reloadRequested = true
    if (releaseTimer) clearTimeout(releaseTimer)
    window.location.reload()
  }
}

function resumed() {
  if (document.visibilityState !== 'visible') return
  if (Date.now() - lastCheck >= 60_000) void checkForUpdates()
  if (available.value) void applyUpdate()
}

function networkChanged() {
  online.value = navigator.onLine !== false
  if (online.value) {
    void checkForUpdates()
    void applyUpdate()
  }
}

function focusChanged() {
  queueMicrotask(() => {
    if (started && available.value) void applyUpdate()
  })
}
function compositionStarted() {
  composing = true
}
function compositionEnded() {
  composing = false
  focusChanged()
}

export function initPwaClient(enabled = import.meta.env.PROD): void {
  if (started || typeof window === 'undefined') return
  online.value = navigator.onLine !== false
  started = true
  generation++
  window.addEventListener('online', networkChanged)
  window.addEventListener('offline', networkChanged)
  cleanups.push(
    () => window.removeEventListener('online', networkChanged),
    () => window.removeEventListener('offline', networkChanged),
  )
  if (!enabled || !window.isSecureContext || !('serviceWorker' in navigator)) return
  registrationEnabled = true

  hadController = !!navigator.serviceWorker.controller
  navigator.serviceWorker.addEventListener('message', workerMessage)
  navigator.serviceWorker.addEventListener('controllerchange', controllerChanged)
  window.addEventListener('focus', resumed)
  document.addEventListener('visibilitychange', resumed)
  document.addEventListener('focusout', focusChanged)
  document.addEventListener('compositionstart', compositionStarted)
  document.addEventListener('compositionend', compositionEnded)
  cleanups.push(
    () => navigator.serviceWorker.removeEventListener('message', workerMessage),
    () => navigator.serviceWorker.removeEventListener('controllerchange', controllerChanged),
    () => window.removeEventListener('focus', resumed),
    () => document.removeEventListener('visibilitychange', resumed),
    () => document.removeEventListener('focusout', focusChanged),
    () => document.removeEventListener('compositionstart', compositionStarted),
    () => document.removeEventListener('compositionend', compositionEnded),
  )
  stopActivityWatch = watch(
    canReloadFromActivity,
    () => {
      if (available.value) void applyUpdate()
    },
    { flush: 'sync' },
  )
  checkTimer = setInterval(resumed, CHECK_INTERVAL)
  retryTimer = setInterval(() => {
    if (available.value) void applyUpdate()
  }, RETRY_INTERVAL)
  void checkForUpdates()
}

export function disposePwaClient(): void {
  started = false
  registrationEnabled = false
  generation++
  for (const cleanup of cleanups.splice(0)) cleanup()
  stopActivityWatch?.()
  stopActivityWatch = undefined
  if (checkTimer) clearInterval(checkTimer)
  if (retryTimer) clearInterval(retryTimer)
  setApplying(false)
  registration = undefined
  available.value = false
  blocked.value = false
  error.value = ''
  checking = false
  requestingActivation = false
  composing = false
  reloadRequested = false
  reloadPending = false
  lastCheck = 0
}
