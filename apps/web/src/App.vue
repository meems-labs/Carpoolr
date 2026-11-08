<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { clientsForPerson } from './api/application'
import OperationStatus from './components/OperationStatus.vue'
import NotificationBell from './components/NotificationBell.vue'
import PwaControls from './components/PwaControls.vue'
import { updateApplying } from './pwa/client'
import { errorMessage } from './composables/useOperation'
import { currentProfile, loadPeople, people, selectedPersonId, selectPerson } from './state/session'

const loading = ref(true)
const error = ref('')
const profileLoading = ref(false)
const route = useRoute()
let controller = new AbortController()
let profileController = new AbortController()

async function initialize() {
  controller.abort()
  controller = new AbortController()
  loading.value = true
  error.value = ''
  try {
    await loadPeople(controller.signal)
  } catch (cause) {
    if (!controller.signal.aborted) error.value = errorMessage(cause)
  } finally {
    loading.value = false
  }
}

async function refreshProfile() {
  profileController.abort()
  profileController = new AbortController()
  const callController = profileController
  const personId = selectedPersonId.value
  if (!personId) {
    profileLoading.value = false
    return
  }
  profileLoading.value = true
  error.value = ''
  try {
    const response = await clientsForPerson(personId).profiles.getMyProfile(
      {},
      { signal: callController.signal, timeoutMs: 10_000 },
    )
    if (personId === selectedPersonId.value && profileController === callController)
      currentProfile.value = response.profile
  } catch (cause) {
    if (personId === selectedPersonId.value && !callController.signal.aborted)
      error.value = errorMessage(cause)
  } finally {
    if (profileController === callController) profileLoading.value = false
  }
}

watch(selectedPersonId, refreshProfile)
onMounted(initialize)
onBeforeUnmount(() => {
  controller.abort()
  profileController.abort()
})

function select(event: Event) {
  selectPerson((event.target as HTMLSelectElement).value)
}
</script>

<template>
  <a class="skip-link" href="#content">Zum Inhalt</a>
  <header class="app-header" data-pwa-update-lock :inert="updateApplying">
    <div class="container header-row">
      <RouterLink class="brand" to="/">
        Carpoolr
        <small>Fahrgemeinschaften fürs Team</small>
      </RouterLink>
      <div class="header-controls">
        <div class="person-select">
          <span class="badge warning">Testmodus</span>
          <label>
            Person auswählen
            <select
              data-pwa-saved-selection
              :value="selectedPersonId"
              :disabled="loading"
              @change="select"
            >
              <option value="">Bitte auswählen …</option>
              <option v-for="person in people" :key="person.id" :value="person.id">
                {{ person.displayName }}
              </option>
            </select>
          </label>
        </div>
        <NotificationBell v-if="selectedPersonId" :key="selectedPersonId" />
      </div>
    </div>
    <nav class="container nav" aria-label="Hauptnavigation">
      <RouterLink to="/">Fahrten finden</RouterLink>
      <RouterLink to="/fahrten/neu">Fahrt anbieten</RouterLink>
      <RouterLink to="/meine-fahrten">Meine Angebote</RouterLink>
      <RouterLink to="/meine-anfragen">Meine Anfragen</RouterLink>
      <RouterLink to="/eingegangene-anfragen">Eingegangene Anfragen</RouterLink>
      <RouterLink to="/kosten">Meine Kosten</RouterLink>
      <RouterLink to="/profil">Mein Profil</RouterLink>
    </nav>
  </header>
  <div class="container"><PwaControls /></div>
  <main id="content" class="container" tabindex="-1" data-pwa-update-lock :inert="updateApplying">
    <OperationStatus :error="error" />
    <div v-if="loading || profileLoading" class="card empty" role="status">
      Daten werden geladen …
    </div>
    <div v-else-if="error && !currentProfile" class="card empty">
      <p>Die Verbindung konnte nicht hergestellt werden.</p>
      <button @click="selectedPersonId ? refreshProfile() : initialize()">Erneut versuchen</button>
    </div>
    <section v-else-if="!selectedPersonId" class="card empty">
      <h1>Gemeinsam unterwegs</h1>
      <p>Wähle oben eine Person aus, um Fahrten anzubieten oder bei Kollegen mitzufahren.</p>
      <p class="small">
        Im Testmodus ist keine Anmeldung nötig. Deine Auswahl bestimmt, wessen Daten du bearbeitest.
      </p>
    </section>
    <RouterView v-else :key="`${selectedPersonId}:${route.path}`" />
  </main>
  <footer class="container footer">
    Carpoolr · Uhrzeiten in Europe/Berlin · Testbetrieb ohne Anmeldung
  </footer>
</template>

<style scoped>
.header-controls {
  display: flex;
  align-items: center;
  gap: 1rem;
}
@media (max-width: 640px) {
  .header-controls {
    width: 100%;
  }
  .person-select {
    flex: 1;
    min-width: 0;
  }
}
.skip-link {
  position: absolute;
  left: 1rem;
  top: -100px;
  padding: 0.6rem 1rem;
  background: white;
  z-index: 10;
}
.skip-link:focus {
  top: 1rem;
}
</style>
