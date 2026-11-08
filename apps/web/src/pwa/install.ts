import { readonly, ref } from 'vue'
import { beginOperation } from '../state/activity'

interface InstallChoice {
  outcome: 'accepted' | 'dismissed'
  platform: string
}

interface InstallPromptEvent extends Event {
  prompt(): Promise<void>
  userChoice: Promise<InstallChoice>
}

type InstallDisplayMode = Pick<
  MediaQueryList,
  'matches' | 'addEventListener' | 'removeEventListener'
>

export interface InstallBrowser extends EventTarget {
  navigator: {
    userAgent: string
    platform: string
    maxTouchPoints: number
    standalone?: boolean
  }
  matchMedia(query: string): InstallDisplayMode
}

export type InstallResult = 'accepted' | 'dismissed' | 'unavailable' | 'failed'

// Browsers decide when installation is available. Keep their one-use prompt only
// until an explicit user click; iOS instead exposes installation in its Share menu.
export function createInstallController(browser?: InstallBrowser) {
  const installed = ref(false)
  const canInstall = ref(false)
  const installing = ref(false)
  const installError = ref('')
  const isIos = ref(
    !!browser &&
      (/iPad|iPhone|iPod/.test(browser.navigator.userAgent) ||
        (browser.navigator.platform === 'MacIntel' && browser.navigator.maxTouchPoints > 1)),
  )
  const displayMode = browser?.matchMedia('(display-mode: standalone)')
  let deferredPrompt: InstallPromptEvent | undefined
  let started = false
  let generation = 0

  function refreshStandalone() {
    // appinstalled may fire in a normal browser tab. Do not undo that evidence
    // just because this tab continues to use the browser display mode.
    if (displayMode?.matches || browser?.navigator.standalone === true) markInstalled()
  }

  function markInstalled() {
    installed.value = true
    canInstall.value = false
    deferredPrompt = undefined
    installError.value = ''
  }

  function beforeInstall(event: Event) {
    const prompt = event as InstallPromptEvent
    if (typeof prompt.prompt !== 'function' || !prompt.userChoice) return
    event.preventDefault()
    if (installed.value) return
    deferredPrompt = prompt
    canInstall.value = true
    installError.value = ''
  }

  function start() {
    if (!browser || started) return
    started = true
    refreshStandalone()
    browser.addEventListener('beforeinstallprompt', beforeInstall)
    browser.addEventListener('appinstalled', markInstalled)
    displayMode?.addEventListener('change', refreshStandalone)
  }

  function dispose() {
    if (!browser || !started) return
    started = false
    generation++
    browser.removeEventListener('beforeinstallprompt', beforeInstall)
    browser.removeEventListener('appinstalled', markInstalled)
    displayMode?.removeEventListener('change', refreshStandalone)
    deferredPrompt = undefined
    canInstall.value = false
    installing.value = false
  }

  async function promptInstall(): Promise<InstallResult> {
    const prompt = deferredPrompt
    if (!prompt || installing.value || installed.value) return 'unavailable'
    // A browser prompt cannot be reused, including after dismissal or failure.
    deferredPrompt = undefined
    canInstall.value = false
    installing.value = true
    installError.value = ''
    const callGeneration = generation
    const finish = beginOperation()
    try {
      await prompt.prompt()
      const choice = await prompt.userChoice
      return choice.outcome
    } catch {
      if (generation === callGeneration)
        installError.value = 'Die Installation konnte nicht geöffnet werden. Nutze das Browsermenü.'
      return 'failed'
    } finally {
      finish()
      if (generation === callGeneration) installing.value = false
    }
  }

  return {
    installed: readonly(installed),
    canInstall: readonly(canInstall),
    installing: readonly(installing),
    installError: readonly(installError),
    isIos: readonly(isIos),
    start,
    dispose,
    promptInstall,
  }
}

const controller = createInstallController(typeof window === 'undefined' ? undefined : window)

export const { installed, canInstall, installing, installError, isIos, promptInstall } = controller
let installConsumers = 0
export function initInstall() {
  installConsumers++
  if (installConsumers === 1) controller.start()
}
export function disposeInstall() {
  if (installConsumers === 0) return
  installConsumers--
  if (installConsumers === 0) controller.dispose()
}
