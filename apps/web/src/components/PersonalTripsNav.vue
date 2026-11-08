<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import { personalTripsNavigation } from '../lib/navigation'
import { updateApplying } from '../pwa/client'
const route = useRoute()
</script>

<template>
  <nav
    class="personal-trips-navigation"
    aria-label="Meine Fahrten"
    data-pwa-update-lock
    :inert="updateApplying"
  >
    <RouterLink
      v-for="item in personalTripsNavigation"
      :key="item.to"
      :to="item.to"
      class="personal-trips-link"
      :class="{ 'is-current': route.path === item.to }"
      :aria-current="route.path === item.to ? 'page' : undefined"
    >
      {{ item.label }}
    </RouterLink>
  </nav>
</template>

<style scoped>
.personal-trips-navigation {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 4px;
  max-width: 480px;
  padding: 5px;
  margin: 0 0 28px;
  border-radius: 16px;
  background: var(--surface-muted);
}
.personal-trips-link {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 44px;
  padding: 10px 8px;
  color: var(--muted, #64736a);
  border-radius: 12px;
  text-decoration: none;
  font-size: 0.82rem;
  line-height: 1.25;
  font-weight: 650;
}
.personal-trips-link:hover {
  color: var(--ink, #142d25);
}
.personal-trips-link.is-current {
  color: var(--ink, #142d25);
  background: var(--surface, #fff);
  box-shadow: var(--tab-shadow);
  font-weight: 800;
}
.personal-trips-link:focus-visible {
  outline: 3px solid var(--brand, #176c56);
  outline-offset: 2px;
}
</style>
