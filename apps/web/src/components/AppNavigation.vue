<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { activeNavigation, mainNavigation } from '../lib/navigation'
import { updateApplying } from '../pwa/client'
import AppIcon from './AppIcon.vue'

defineProps<{ variant: 'mobile' | 'desktop' }>()
const route = useRoute()
const active = computed(() => activeNavigation(route.path))
</script>

<template>
  <nav
    class="app-navigation"
    :class="`app-navigation--${variant}`"
    aria-label="Hauptnavigation"
    data-pwa-update-lock
    :inert="updateApplying"
  >
    <ul class="navigation-list">
      <li v-for="item in mainNavigation" :key="item.id">
        <RouterLink
          :to="item.to"
          class="navigation-link"
          :class="{ 'is-current': active === item.id }"
          :aria-current="active === item.id ? 'page' : undefined"
        >
          <span class="navigation-symbol"><AppIcon :name="item.icon" :size="22" /></span>
          <span class="navigation-label">{{ item.label }}</span>
          <AppIcon
            v-if="variant === 'desktop' && active === item.id"
            name="chevron-right"
            :size="16"
            class="navigation-arrow"
          />
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.app-navigation--desktop {
  display: none;
}
.navigation-list {
  margin: 0;
  padding: 0;
  list-style: none;
}
.app-navigation--mobile {
  position: fixed;
  inset: auto 0 0;
  z-index: 50;
  padding: 8px max(8px, env(safe-area-inset-right)) calc(8px + env(safe-area-inset-bottom))
    max(8px, env(safe-area-inset-left));
  background: var(--surface, #fff);
  border-top: 1px solid var(--line, #e4e8e1);
  box-shadow: 0 -8px 32px #142d2508;
}
.app-navigation--mobile .navigation-list {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  max-width: 660px;
  margin: 0 auto;
  gap: 2px;
}
.navigation-link {
  color: var(--muted, #64736a);
  text-decoration: none;
  -webkit-tap-highlight-color: transparent;
}
.app-navigation--mobile .navigation-link {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  min-height: 58px;
  gap: 4px;
  border-radius: 14px;
  padding: 3px 0;
}
.navigation-symbol {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
}
.app-navigation--mobile .navigation-symbol {
  width: 46px;
  height: 29px;
  border-radius: 18px;
  transition: background 140ms ease;
}
.app-navigation--mobile .is-current .navigation-symbol {
  color: var(--brand, #176c56);
  background: var(--accent, #ddf8a3);
}
.navigation-label {
  max-width: 100%;
  overflow-wrap: anywhere;
  font-size: clamp(0.61rem, 2.7vw, 0.73rem);
  line-height: 1.2;
  font-weight: 650;
  text-align: center;
}
.is-current {
  color: var(--brand, #176c56);
}
.is-current .navigation-label {
  font-weight: 800;
}
.navigation-link:focus-visible {
  outline: 3px solid var(--brand, #176c56);
  outline-offset: 2px;
}
@media (min-width: 1024px) {
  .app-navigation--mobile {
    display: none;
  }
  .app-navigation--desktop {
    display: block;
  }
  .app-navigation--desktop .navigation-list {
    display: grid;
    gap: 7px;
  }
  .app-navigation--desktop .navigation-link {
    display: flex;
    align-items: center;
    min-height: 52px;
    gap: 13px;
    padding: 12px 14px;
    border-radius: 14px;
  }
  .app-navigation--desktop .navigation-label {
    font-size: 0.88rem;
    font-weight: 650;
    text-align: left;
  }
  .app-navigation--desktop .navigation-link:hover {
    color: var(--ink, #142d25);
    background: var(--paper, #f5f5ef);
  }
  .app-navigation--desktop .is-current,
  .app-navigation--desktop .is-current:hover {
    color: #fff;
    background: var(--brand, #176c56);
  }
  .navigation-arrow {
    margin-left: auto;
    color: var(--accent, #ddf8a3);
  }
}
@media (prefers-reduced-motion: reduce) {
  .navigation-symbol {
    transition: none;
  }
}
</style>
