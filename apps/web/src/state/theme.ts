import { computed, ref } from 'vue'

export type ThemePreference = 'system' | 'light' | 'dark'
export const themeStorageKey = 'carpoolr.theme'
export const themePreference = ref<ThemePreference>('system')
const systemDark = ref(false)
export const resolvedTheme = computed(() =>
  themePreference.value === 'system'
    ? systemDark.value
      ? 'dark'
      : 'light'
    : themePreference.value,
)

function parsePreference(value: string | null): ThemePreference {
  return value === 'light' || value === 'dark' ? value : 'system'
}

function applyTheme() {
  const theme = resolvedTheme.value
  document.documentElement.dataset.theme = theme
  document.documentElement.style.colorScheme = theme
  const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (meta) meta.content = theme === 'dark' ? '#111b17' : '#f5f4ef'
}

export function setThemePreference(preference: ThemePreference) {
  themePreference.value = parsePreference(preference)
  try {
    localStorage.setItem(themeStorageKey, themePreference.value)
  } catch {
    // A blocked or full storage must not prevent a theme change.
  }
  applyTheme()
}

export function initTheme() {
  const media = window.matchMedia('(prefers-color-scheme: dark)')
  systemDark.value = media.matches
  try {
    themePreference.value = parsePreference(localStorage.getItem(themeStorageKey))
  } catch {
    themePreference.value = 'system'
  }
  applyTheme()
  function onSystemChange(event: MediaQueryListEvent) {
    systemDark.value = event.matches
    applyTheme()
  }
  function onStorage(event: StorageEvent) {
    if (event.key !== themeStorageKey && event.key !== null) return
    themePreference.value = parsePreference(event.newValue)
    applyTheme()
  }
  media.addEventListener('change', onSystemChange)
  window.addEventListener('storage', onStorage)
  return () => {
    media.removeEventListener('change', onSystemChange)
    window.removeEventListener('storage', onStorage)
  }
}
