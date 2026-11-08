// These tests inspect shipped assets in the Node test runtime; app typings remain browser-only.
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  initTheme,
  resolvedTheme,
  setThemePreference,
  themePreference,
  themeStorageKey,
} from './theme'
const disposers = []
function environment(saved = null, dark = false) {
  const values = new Map(saved === null ? [] : [[themeStorageKey, saved]])
  const mediaListeners = new Set()
  const storageListeners = new Set()
  const media = {
    matches: dark,
    addEventListener: vi.fn((_type, listener) => mediaListeners.add(listener)),
    removeEventListener: vi.fn((_type, listener) => mediaListeners.delete(listener)),
  }
  const meta = { content: '' }
  const document = {
    documentElement: { dataset: {}, style: { colorScheme: '' } },
    querySelector: () => meta,
  }
  const localStorage = {
    getItem: vi.fn((key) => values.get(key) ?? null),
    setItem: vi.fn((key, value) => values.set(key, value)),
  }
  const matchMedia = vi.fn(() => media)
  const window = {
    matchMedia,
    addEventListener: vi.fn((_type, listener) => storageListeners.add(listener)),
    removeEventListener: vi.fn((_type, listener) => storageListeners.delete(listener)),
  }
  vi.stubGlobal('document', document)
  vi.stubGlobal('window', window)
  vi.stubGlobal('localStorage', localStorage)
  const start = () => disposers.push(initTheme())
  return {
    document,
    localStorage,
    meta,
    values,
    mediaListeners,
    storageListeners,
    start,
    bootstrap() {
      runInNewContext(
        readFileSync(new URL('../../public/theme-init.js', import.meta.url), 'utf8'),
        { document, localStorage, matchMedia },
      )
    },
    systemChange(matches) {
      for (const listener of mediaListeners) listener({ matches })
    },
    storageChange(key, newValue) {
      for (const listener of storageListeners) listener({ key, newValue })
    },
  }
}
afterEach(() => {
  for (const dispose of disposers.splice(0)) dispose()
  vi.unstubAllGlobals()
})
describe('device theme preference', () => {
  it('starts with the system and follows a later device change', () => {
    const env = environment(null, true)
    env.start()
    expect(themePreference.value).toBe('system')
    expect(resolvedTheme.value).toBe('dark')
    expect(env.document.documentElement.dataset.theme).toBe('dark')
    expect(env.meta.content).toBe('#111b17')
    env.systemChange(false)
    expect(resolvedTheme.value).toBe('light')
    expect(env.document.documentElement.style.colorScheme).toBe('light')
    expect(env.meta.content).toBe('#f5f4ef')
  })
  it('persists an explicit selection and ignores system changes until system is chosen', () => {
    const env = environment(null, false)
    env.start()
    setThemePreference('dark')
    env.systemChange(false)
    expect(resolvedTheme.value).toBe('dark')
    expect(env.values.get(themeStorageKey)).toBe('dark')
    setThemePreference('system')
    expect(resolvedTheme.value).toBe('light')
    expect(env.values.get(themeStorageKey)).toBe('system')
  })
  it.each(['light', 'dark'])('restores saved %s independently of the system', (saved) => {
    const env = environment(saved, saved !== 'dark')
    env.start()
    expect(resolvedTheme.value).toBe(saved)
    expect(env.document.documentElement.dataset.theme).toBe(saved)
  })
  it('treats an unknown saved value as system', () => {
    const env = environment('unexpected', true)
    env.start()
    expect(themePreference.value).toBe('system')
    expect(resolvedTheme.value).toBe('dark')
  })
  it('works when browser storage is blocked', () => {
    const env = environment(null, true)
    env.localStorage.getItem.mockImplementation(() => {
      throw new Error('blocked')
    })
    env.localStorage.setItem.mockImplementation(() => {
      throw new Error('blocked')
    })
    env.start()
    expect(resolvedTheme.value).toBe('dark')
    expect(() => setThemePreference('light')).not.toThrow()
    expect(env.document.documentElement.dataset.theme).toBe('light')
  })
  it('syncs selections and clearing storage across windows without writing them back', () => {
    const env = environment('light', true)
    env.start()
    env.storageChange('other-key', 'dark')
    expect(resolvedTheme.value).toBe('light')
    env.storageChange(themeStorageKey, 'dark')
    expect(resolvedTheme.value).toBe('dark')
    env.storageChange(themeStorageKey, null)
    expect(themePreference.value).toBe('system')
    env.storageChange(null, null)
    expect(resolvedTheme.value).toBe('dark')
    expect(env.localStorage.setItem).not.toHaveBeenCalled()
  })
  it('releases its device and storage listeners', () => {
    const env = environment()
    env.start()
    disposers.pop()()
    expect(env.mediaListeners.size).toBe(0)
    expect(env.storageListeners.size).toBe(0)
  })
  it.each([
    [null, false],
    [null, true],
    ['light', true],
    ['dark', false],
    ['invalid', true],
  ])('uses the same theme before Vue starts (saved=%s, systemDark=%s)', (saved, dark) => {
    const env = environment(saved, dark)
    env.bootstrap()
    const bootTheme = env.document.documentElement.dataset.theme
    const bootChrome = env.meta.content
    env.start()
    expect(env.document.documentElement.dataset.theme).toBe(bootTheme)
    expect(env.meta.content).toBe(bootChrome)
  })
})
function luminance(hex) {
  const value =
    hex.length === 4
      ? hex
          .slice(1)
          .split('')
          .map((n) => n + n)
          .join('')
      : hex.slice(1)
  const channels = [0, 2, 4]
    .map((i) => parseInt(value.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
}
describe('theme contrast', () => {
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8')
  const tokenBlock = (source) =>
    Object.fromEntries(
      [...source.matchAll(/--([\w-]+):\s*(#[\da-f]+);/gi)].map((m) => [m[1], m[2]]),
    )
  const light = tokenBlock(css.match(/:root \{([\s\S]*?)\r?\n\}/)[1])
  const dark = {
    ...light,
    ...tokenBlock(css.match(/:root\[data-theme='dark'\] \{([\s\S]*?)\r?\n\}/)[1]),
  }
  const pairs = [
    ['ink', 'paper'],
    ['ink', 'surface'],
    ['muted', 'paper'],
    ['muted', 'surface'],
    ['brand', 'paper'],
    ['brand', 'surface'],
    ['on-brand', 'brand'],
    ['on-accent', 'accent'],
    ['success-ink', 'success-surface'],
    ['warning-ink', 'warning-surface'],
    ['danger-ink', 'danger-surface'],
    ['notice-ink', 'notice-surface'],
    ['badge-ink', 'badge-surface'],
    ['emphasis-ink', 'emphasis-surface'],
    ['emphasis-muted', 'emphasis-surface'],
  ]
  it.each([
    ['light', light],
    ['dark', dark],
  ])('%s text tokens reach WCAG AA contrast', (_theme, tokens) => {
    for (const [foreground, background] of pairs) {
      const a = luminance(tokens[foreground])
      const b = luminance(tokens[background])
      const contrast = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
      expect(contrast, `${foreground} on ${background}`).toBeGreaterThanOrEqual(4.5)
    }
  })
})
