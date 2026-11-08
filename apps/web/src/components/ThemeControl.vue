<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import {
  initTheme,
  setThemePreference,
  themePreference,
  type ThemePreference,
} from '../state/theme'

let dispose: (() => void) | undefined
onMounted(() => (dispose = initTheme()))
onBeforeUnmount(() => dispose?.())

function selectTheme(event: Event) {
  setThemePreference((event.target as HTMLSelectElement).value as ThemePreference)
}
</script>

<template>
  <label class="theme-control">
    Farbschema
    <select :value="themePreference" data-pwa-saved-selection @change="selectTheme">
      <option value="system">System</option>
      <option value="light">Hell</option>
      <option value="dark">Dunkel</option>
    </select>
  </label>
</template>

<style scoped>
.theme-control {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
  max-width: 100%;
  color: var(--muted);
  font-size: 0.75rem;
  font-weight: 600;
}
.theme-control select {
  width: auto;
  max-width: 100%;
  min-height: 44px;
  padding: 0.5rem 0.7rem;
  font-size: 0.8rem;
}
</style>
