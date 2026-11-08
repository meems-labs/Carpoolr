import { describe, expect, it, vi } from 'vitest'
import { createInstallController, type InstallBrowser } from './install'
import { activeOperationCount } from '../state/activity'

class FakeDisplayMode extends EventTarget {
  matches = false
}

class FakeBrowser extends EventTarget implements InstallBrowser {
  navigator = {
    userAgent: 'Chrome',
    platform: 'Linux',
    maxTouchPoints: 0,
    standalone: false,
  }
  displayMode = new FakeDisplayMode()

  matchMedia() {
    return this.displayMode
  }
}

function installEvent(outcome: 'accepted' | 'dismissed' = 'accepted') {
  return Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
    prompt: vi.fn().mockResolvedValue(undefined),
    userChoice: Promise.resolve({ outcome, platform: 'web' }),
  })
}

describe('native installation', () => {
  it('blocks automatic reload until an open installation dialog has settled', async () => {
    const browser = new FakeBrowser()
    const install = createInstallController(browser)
    install.start()
    let choose!: (choice: { outcome: 'dismissed'; platform: string }) => void
    const event = Object.assign(installEvent(), {
      userChoice: new Promise<{ outcome: 'dismissed'; platform: string }>((resolve) => {
        choose = resolve
      }),
    })
    browser.dispatchEvent(event)
    const result = install.promptInstall()
    expect(activeOperationCount.value).toBe(1)
    choose({ outcome: 'dismissed', platform: 'web' })
    expect(await result).toBe('dismissed')
    expect(activeOperationCount.value).toBe(0)
    install.dispose()
  })
  it('exposes a one-use prompt only after the browser offers installation', async () => {
    const browser = new FakeBrowser()
    const install = createInstallController(browser)
    install.start()
    expect(install.canInstall.value).toBe(false)
    expect(await install.promptInstall()).toBe('unavailable')

    const event = installEvent('dismissed')
    browser.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(true)
    expect(install.canInstall.value).toBe(true)
    expect(event.prompt).not.toHaveBeenCalled()
    expect(await install.promptInstall()).toBe('dismissed')
    expect(install.canInstall.value).toBe(false)
    expect(await install.promptInstall()).toBe('unavailable')
    expect(event.prompt).toHaveBeenCalledTimes(1)

    // Dismissal does not invent a second prompt; a later browser event can offer one.
    browser.dispatchEvent(installEvent())
    expect(install.canInstall.value).toBe(true)
    install.dispose()
  })

  it('waits for installation evidence and clears an outstanding prompt on appinstalled', async () => {
    const browser = new FakeBrowser()
    const install = createInstallController(browser)
    install.start()
    browser.dispatchEvent(installEvent())
    expect(await install.promptInstall()).toBe('accepted')
    expect(install.installed.value).toBe(false)

    browser.dispatchEvent(installEvent())
    browser.dispatchEvent(new Event('appinstalled'))
    expect(install.installed.value).toBe(true)
    expect(install.canInstall.value).toBe(false)
    browser.displayMode.dispatchEvent(new Event('change'))
    expect(install.installed.value).toBe(true)
    expect(await install.promptInstall()).toBe('unavailable')
    install.dispose()
  })

  it('detects iPhone and iPad standalone launches without claiming installation in browser mode', () => {
    const browser = new FakeBrowser()
    browser.navigator.userAgent = 'iPhone'
    const iphone = createInstallController(browser)
    iphone.start()
    expect(iphone.isIos.value).toBe(true)
    expect(iphone.installed.value).toBe(false)
    iphone.dispose()

    browser.navigator.userAgent = 'Macintosh'
    browser.navigator.platform = 'MacIntel'
    browser.navigator.maxTouchPoints = 5
    browser.navigator.standalone = true
    const ipad = createInstallController(browser)
    ipad.start()
    expect(ipad.isIos.value).toBe(true)
    expect(ipad.installed.value).toBe(true)
    ipad.dispose()
  })

  it('recognizes display-mode changes and removes browser listeners on disposal', () => {
    const browser = new FakeBrowser()
    const install = createInstallController(browser)
    install.start()
    install.start()
    browser.displayMode.matches = true
    browser.displayMode.dispatchEvent(new Event('change'))
    expect(install.installed.value).toBe(true)
    install.dispose()

    const event = installEvent()
    browser.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(false)
    expect(install.canInstall.value).toBe(false)
  })

  it('consumes failed prompts and offers a safe browser-menu fallback', async () => {
    const browser = new FakeBrowser()
    const install = createInstallController(browser)
    install.start()
    const event = installEvent()
    event.prompt.mockRejectedValue(new Error('NotAllowedError'))
    browser.dispatchEvent(event)
    expect(await install.promptInstall()).toBe('failed')
    expect(install.installError.value).toContain('Browsermenü')
    expect(install.canInstall.value).toBe(false)
    expect(install.installing.value).toBe(false)
    install.dispose()
  })
})
