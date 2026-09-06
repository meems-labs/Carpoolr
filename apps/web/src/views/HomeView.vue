<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { health, type Health } from '../api/client'

const apiStatus = ref<Health | null>(null)
const apiError = ref<string | null>(null)

onMounted(async () => {
  try {
    apiStatus.value = await health()
  } catch (err) {
    apiError.value = err instanceof Error ? err.message : String(err)
  }
})
</script>

<template>
  <section>
    <h1>Carpoolr</h1>
    <p>Mitfahr-App fürs Team — im Aufbau.</p>
    <p v-if="apiStatus" class="ok">API: verbunden ({{ apiStatus.status }})</p>
    <p v-else-if="apiError" class="err">API: nicht erreichbar ({{ apiError }})</p>
    <p v-else>API: verbinde…</p>
  </section>
</template>

<style scoped>
.ok {
  color: darkgreen;
}
.err {
  color: darkred;
}
</style>