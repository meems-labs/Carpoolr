import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref, type EffectScope } from 'vue'
import { useUnsavedChanges } from '../composables/useUnsavedChanges'
import { beginOperation } from '../state/activity'
import {
  applyUpdate,
  checkForUpdates,
  disposePwaClient,
  initPwaClient,
  isOnline,
  updateApplying,
  updateAvailable,
  updateBlocked,
  updateError,
} from './client'

class MockPort {
  peer?: MockPort
  onmessage: ((event: { data: unknown }) => void) | null = null
  closed = false

  postMessage(data: unknown) {
    const receiver = this.peer
    void Promise.resolve().then(() => {
      if (receiver && !receiver.closed) receiver.onmessage?.({ data })
    })
  }

  close() {
    this.closed = true
  }
}

class MockMessageChannel {
  port1 = new MockPort()
  port2 = new MockPort()

  constructor() {
    this.port1.peer = this.port2
    this.port2.peer = this.port1
  }
}

class MockWorker extends EventTarget {
  scriptURL = 'https://carpoolr.test/sw.js'
  state = 'installed'
  activationStatus = 'checking'
  postMessage = vi.fn((message: unknown, ports: MockPort[] = []) => {
    if (
      message &&
      typeof message === 'object' &&
      'type' in message &&
      message.type === 'ACTIVATE_WHEN_SAFE'
    ) {
      ports[0]?.postMessage({ status: this.activationStatus })
    }
  })
}

class MockRegistration extends EventTarget {
  active: MockWorker | null = new MockWorker()
  waiting: MockWorker | null = null
  installing: MockWorker | null = null
  update = vi.fn(async () => undefined)
}

class MockServiceWorkers extends EventTarget {
  controller: MockWorker | null = new MockWorker()
  register = vi.fn(async () => registration)
}

let registration: MockRegistration
let workers: MockServiceWorkers
let browser: EventTarget & {
  isSecureContext: boolean
  location: { origin: string; reload: ReturnType<typeof vi.fn> }
}
let page: EventTarget & {
  activeElement: {
    tagName: string
    isContentEditable?: boolean
    closest?: (selector: string) => object | null
  } | null
  visibilityState: string
  querySelectorAll: ReturnType<typeof vi.fn>
}
let network: { onLine: boolean; serviceWorker: MockServiceWorkers }
let header: { inert: boolean }
let main: { inert: boolean }
const scopes: EffectScope[] = []
const operationCleanups: Array<() => void> = []

function emit(target: EventTarget, type: string, values: Record<string, unknown> = {}) {
  target.dispatchEvent(Object.assign(new Event(type), values))
}

async function settle() {
  for (let index = 0; index < 6; index++) {
    await Promise.resolve()
    vi.runAllTicks()
    await nextTick()
  }
}

async function start() {
  initPwaClient(true)
  await settle()
}

function dirtyForm() {
  const input = ref('Original input')
  const scope = effectScope()
  scopes.push(scope)
  const changes = scope.run(() => useUnsavedChanges(() => input.value))!
  input.value = 'Unsaved input'
  return changes
}

function prepare(worker = new MockWorker(), onReply?: (reply: { safe: boolean }) => void) {
  const port = {
    postMessage: vi.fn((reply: { safe: boolean }) => onReply?.(reply)),
    close: vi.fn(),
  }
  emit(workers, 'message', { source: worker, data: { type: 'PWA_UPDATE_PREPARE' }, ports: [port] })
  return port
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-11-08T12:00:00Z'))
  registration = new MockRegistration()
  workers = new MockServiceWorkers()
  header = { inert: false }
  main = { inert: true }
  browser = Object.assign(new EventTarget(), {
    isSecureContext: true,
    location: { origin: 'https://carpoolr.test', reload: vi.fn() },
  })
  page = Object.assign(new EventTarget(), {
    activeElement: null as { tagName: string; isContentEditable?: boolean } | null,
    visibilityState: 'visible',
    querySelectorAll: vi.fn(() => [header, main]),
  })
  network = { onLine: true, serviceWorker: workers }
  vi.stubGlobal('window', browser)
  vi.stubGlobal('document', page)
  vi.stubGlobal('navigator', network)
  vi.stubGlobal('MessageChannel', MockMessageChannel)
})

afterEach(() => {
  disposePwaClient()
  for (const scope of scopes.splice(0)) scope.stop()
  for (const finish of operationCleanups.splice(0)) finish()
  vi.clearAllTimers()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('automatic PWA client updates', () => {
  it('waits for both saved input and settled operations before contacting a waiting worker', async () => {
    const changes = dirtyForm()
    const finish = beginOperation()
    operationCleanups.push(finish)
    await start()
    const worker = new MockWorker()
    registration.waiting = worker

    await checkForUpdates()
    await settle()
    expect(updateAvailable.value).toBe(true)
    expect(updateBlocked.value).toBe(true)
    expect(worker.postMessage).not.toHaveBeenCalled()

    finish()
    await settle()
    expect(worker.postMessage).not.toHaveBeenCalled()
    changes.markSaved()
    await settle()
    expect(worker.postMessage).toHaveBeenCalledTimes(1)
    expect(worker.postMessage.mock.calls[0][0]).toEqual({ type: 'ACTIVATE_WHEN_SAFE' })
    expect(worker.postMessage.mock.calls[0][1]).toHaveLength(1)
  })

  it('freezes the UI synchronously before acknowledging PREPARE and restores its original inert state on cancellation', async () => {
    await start()
    const port = prepare(new MockWorker(), (reply) => {
      expect(reply).toEqual({ safe: true })
      expect(updateApplying.value).toBe(true)
      expect(header.inert).toBe(true)
      expect(main.inert).toBe(true)
    })
    expect(port.postMessage).toHaveBeenCalledTimes(1)
    expect(port.close).toHaveBeenCalledTimes(1)

    emit(workers, 'message', {
      source: new MockWorker(),
      data: { type: 'PWA_UPDATE_CANCELLED' },
      ports: [],
    })
    expect(updateApplying.value).toBe(false)
    expect(updateBlocked.value).toBe(true)
    expect(header.inert).toBe(false)
    expect(main.inert).toBe(true)
  })

  it('refuses PREPARE when input is dirty and ignores a worker from a different script', async () => {
    dirtyForm()
    await start()
    const port = prepare()
    expect(port.postMessage).toHaveBeenCalledWith({ safe: false })
    expect(updateApplying.value).toBe(false)
    expect(header.inert).toBe(false)
    const unrelated = new MockWorker()
    unrelated.scriptURL = 'https://carpoolr.test/unrelated.js'
    expect(prepare(unrelated).postMessage).not.toHaveBeenCalled()
  })

  it('does not reload on the first controller claim but reloads an approved upgrade exactly once', async () => {
    workers.controller = null
    await start()
    emit(workers, 'controllerchange')
    expect(browser.location.reload).not.toHaveBeenCalled()
    expect(updateAvailable.value).toBe(false)

    prepare()
    emit(workers, 'controllerchange')
    emit(workers, 'controllerchange')
    expect(browser.location.reload).toHaveBeenCalledTimes(1)
  })

  it('does not announce a transient first-install waiting worker as an app update', async () => {
    workers.controller = null
    registration.active = null
    const worker = new MockWorker()
    registration.waiting = worker
    await start()
    expect(updateAvailable.value).toBe(false)
    expect(worker.postMessage).not.toHaveBeenCalled()
    registration.waiting = null
    registration.active = worker
    emit(workers, 'controllerchange')
    expect(updateAvailable.value).toBe(false)
    expect(browser.location.reload).not.toHaveBeenCalled()
  })

  it('defers an unexpected controller change until edits and active operations are both safe', async () => {
    await start()
    const changes = dirtyForm()
    const finish = beginOperation()
    operationCleanups.push(finish)

    emit(workers, 'controllerchange')
    expect(browser.location.reload).not.toHaveBeenCalled()
    expect(updateAvailable.value).toBe(true)
    expect(updateBlocked.value).toBe(true)
    finish()
    await settle()
    expect(browser.location.reload).not.toHaveBeenCalled()
    changes.markSaved()
    await settle()
    expect(browser.location.reload).toHaveBeenCalledTimes(1)
  })

  it('rechecks input safety when activation exceeds the freeze timeout', async () => {
    await start()
    prepare()
    expect(header.inert).toBe(true)
    await vi.advanceTimersByTimeAsync(10_001)
    expect(header.inert).toBe(false)
    const changes = dirtyForm()

    emit(workers, 'controllerchange')
    expect(browser.location.reload).not.toHaveBeenCalled()
    changes.markSaved()
    await settle()
    expect(browser.location.reload).toHaveBeenCalledTimes(1)
  })

  it.each<[string, boolean]>([
    ['INPUT', false],
    ['TEXTAREA', false],
    ['SELECT', false],
    ['DIV', true],
  ])('protects a focused %s editor until focus leaves', async (tagName, isContentEditable) => {
    page.activeElement = { tagName, isContentEditable }
    await start()
    const worker = new MockWorker()
    registration.waiting = worker
    await checkForUpdates()
    await settle()
    expect(updateBlocked.value).toBe(true)
    expect(worker.postMessage).not.toHaveBeenCalled()

    page.activeElement = null
    emit(page, 'focusout')
    await settle()
    expect(worker.postMessage).toHaveBeenCalledTimes(1)
  })

  it('allows updates while the already-persisted demo-person selection still has focus', async () => {
    page.activeElement = { tagName: 'SELECT', closest: () => ({}) }
    await start()
    const worker = new MockWorker()
    registration.waiting = worker
    await checkForUpdates()
    await settle()
    expect(worker.postMessage).toHaveBeenCalledTimes(1)
    expect(updateBlocked.value).toBe(false)
  })

  it('protects an active IME composition until compositionend', async () => {
    await start()
    emit(page, 'compositionstart')
    const worker = new MockWorker()
    registration.waiting = worker
    await checkForUpdates()
    await settle()
    expect(worker.postMessage).not.toHaveBeenCalled()

    emit(page, 'compositionend')
    await settle()
    expect(worker.postMessage).toHaveBeenCalledTimes(1)
  })

  it('checks on reconnect and later focus while skipping network checks offline', async () => {
    await start()
    registration.update.mockClear()
    network.onLine = false
    emit(browser, 'offline')
    expect(isOnline.value).toBe(false)
    await checkForUpdates()
    expect(registration.update).not.toHaveBeenCalled()

    network.onLine = true
    emit(browser, 'online')
    await settle()
    expect(isOnline.value).toBe(true)
    expect(registration.update).toHaveBeenCalledTimes(1)
    vi.setSystemTime(Date.now() + 61_000)
    emit(browser, 'focus')
    await settle()
    expect(registration.update).toHaveBeenCalledTimes(2)
  })

  it('recovers from registration failure on reconnect without an uncaught rejection', async () => {
    workers.register.mockRejectedValueOnce(new Error('Network unavailable'))
    await start()
    expect(updateError.value).not.toBe('')
    network.onLine = false
    emit(browser, 'offline')
    network.onLine = true
    emit(browser, 'online')
    await settle()

    expect(workers.register).toHaveBeenCalledTimes(2)
    expect(registration.update).toHaveBeenCalled()
    expect(updateError.value).toBe('')
  })

  it('handles update failures and clears the error after a successful retry', async () => {
    await start()
    registration.update.mockRejectedValueOnce(new Error('Update unavailable'))
    await expect(checkForUpdates()).resolves.toBeUndefined()
    expect(updateError.value).not.toBe('')
    await checkForUpdates()
    expect(updateError.value).toBe('')
  })

  it('keeps service-worker registration disabled across offline and reconnect events', async () => {
    initPwaClient(false)
    await settle()
    network.onLine = false
    emit(browser, 'offline')
    network.onLine = true
    emit(browser, 'online')
    await checkForUpdates()
    await settle()

    expect(isOnline.value).toBe(true)
    expect(workers.register).not.toHaveBeenCalled()
  })

  it('receives an activation decision through the transferred message port', async () => {
    await start()
    const worker = new MockWorker()
    worker.activationStatus = 'activating'
    registration.waiting = worker
    await applyUpdate()
    expect(updateApplying.value).toBe(true)
    expect(header.inert).toBe(true)
    expect(browser.location.reload).not.toHaveBeenCalled()
  })
})
