<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { clientsForPerson } from './api/application'
import AppIcon from './components/AppIcon.vue'
import AppNavigation from './components/AppNavigation.vue'
import OperationStatus from './components/OperationStatus.vue'
import NotificationBell from './components/NotificationBell.vue'
import PersonalTripsNav from './components/PersonalTripsNav.vue'
import PwaControls from './components/PwaControls.vue'
import { updateApplying } from './pwa/client'
import { errorMessage } from './composables/useOperation'
import { hasPersonalTripsNavigation } from './lib/navigation'
import { currentProfile, loadPeople, people, selectedPersonId, selectPerson } from './state/session'

const loading = ref(true)
const error = ref('')
const profileLoading = ref(false)
const route = useRoute()
const publicHome = computed(() => route.path === '/' && !selectedPersonId.value)
let controller = new AbortController()
let profileController = new AbortController()

async function initialize() {
  controller.abort()
  controller = new AbortController()
  const callController = controller
  loading.value = true
  error.value = ''
  try {
    await loadPeople(callController.signal)
  } catch (cause) {
    if (controller === callController && !callController.signal.aborted)
      error.value = errorMessage(cause)
  } finally {
    if (controller === callController) loading.value = false
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
    if (
      personId === selectedPersonId.value &&
      profileController === callController &&
      !callController.signal.aborted
    )
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
  <div class="app-shell">
    <aside class="shell-sidebar" data-pwa-update-lock :inert="updateApplying">
      <RouterLink class="shell-brand sidebar-brand" to="/" aria-label="Carpoolr – Startseite">
        <span class="brand-mark"><AppIcon name="car" :size="26" /></span>
        <span class="brand-name">
          Carpoolr
          <small>Ein Weg. Gemeinsam.</small>
        </span>
      </RouterLink>
      <div class="sidebar-caption">Dein Alltag</div>
      <AppNavigation variant="desktop" />
      <RouterLink class="sidebar-offer" to="/fahrten/neu">
        <AppIcon name="plus" :size="19" />
        Fahrt anbieten
      </RouterLink>
      <div class="sidebar-bottom">
        <span class="sidebar-note-mark"><AppIcon name="users" :size="22" /></span>
        <p>
          Gute Gesellschaft.
          <br />
          <strong>Ein gemeinsamer Weg.</strong>
        </p>
      </div>
    </aside>

    <div class="shell-column">
      <header class="shell-header" data-pwa-update-lock :inert="updateApplying">
        <RouterLink class="shell-brand mobile-brand" to="/" aria-label="Carpoolr – Startseite">
          <span class="brand-mark"><AppIcon name="car" :size="23" /></span>
          <span class="brand-name">Carpoolr</span>
        </RouterLink>
        <div class="desktop-heading">
          <span>Weniger allein fahren.</span>
          <strong>Mehr zusammen ankommen.</strong>
        </div>
        <div class="header-controls">
          <div class="person-select">
            <div class="person-caption">
              <label for="active-person">Person auswählen</label>
              <span aria-hidden="true">Testmodus</span>
            </div>
            <select
              id="active-person"
              data-person-select
              data-pwa-saved-selection
              :value="selectedPersonId"
              :disabled="loading"
              @change="select"
            >
              <option value="">Bitte auswählen …</option>
              <option v-for="person in people" :key="person.id" :value="person.id">
                {{
                  person.id === selectedPersonId && currentProfile?.displayName
                    ? currentProfile.displayName
                    : person.displayName
                }}
              </option>
            </select>
          </div>
          <NotificationBell v-if="selectedPersonId" :key="selectedPersonId" />
        </div>
      </header>

      <div class="shell-status"><PwaControls mode="status" /></div>

      <main
        id="content"
        class="shell-main"
        tabindex="-1"
        data-pwa-update-lock
        :inert="updateApplying"
      >
        <OperationStatus :error="error" />
        <template v-if="publicHome">
          <div v-if="error" class="connection-retry">
            <button class="secondary" :disabled="loading" @click="initialize">
              Verbindung erneut versuchen
            </button>
          </div>
          <RouterView :key="`${selectedPersonId}:${route.path}`" />
        </template>
        <section v-else-if="loading || profileLoading" class="shell-state" role="status">
          <span class="state-symbol"><AppIcon name="car" :size="28" /></span>
          <h1>Einen Moment noch.</h1>
          <p>Deine Fahrgemeinschaften werden geladen …</p>
        </section>
        <section v-else-if="error && !currentProfile" class="shell-state">
          <span class="state-symbol"><AppIcon name="arrow-right" :size="26" /></span>
          <h1>Gerade keine Verbindung.</h1>
          <p>Versuche es noch einmal, sobald du wieder online bist.</p>
          <button @click="selectedPersonId ? refreshProfile() : initialize()">
            Erneut versuchen
          </button>
        </section>
        <section v-else-if="!selectedPersonId" class="shell-state">
          <span class="state-symbol"><AppIcon name="users" :size="28" /></span>
          <h1>Wer fährt heute mit?</h1>
          <p>
            Wähle oben eine Person aus, um Fahrten zu finden und deine gemeinsamen Wege zu planen.
          </p>
          <p class="state-hint">Im Testmodus bestimmt die Auswahl, wessen Daten du bearbeitest.</p>
        </section>
        <template v-else>
          <PersonalTripsNav v-if="hasPersonalTripsNavigation(route.path)" />
          <RouterView :key="`${selectedPersonId}:${route.path}`" />
        </template>
      </main>
      <footer class="shell-footer">
        Gemeinsam unterwegs.
        <span>Zeiten in Europe/Berlin.</span>
      </footer>
    </div>
    <AppNavigation variant="mobile" />
  </div>
</template>

<style scoped>
.app-shell {
  min-height: 100dvh;
}
.shell-sidebar {
  display: none;
}
.shell-column {
  display: flex;
  flex-direction: column;
  min-height: 100dvh;
  min-width: 0;
  padding-bottom: calc(84px + env(safe-area-inset-bottom));
}
.shell-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 92px;
  gap: 12px;
  padding: 15px 16px;
  border-bottom: 1px solid var(--line, #e4e8e1);
  background: var(--paper, #f8f8f2);
}
.shell-brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--ink, #142d25);
  text-decoration: none;
  flex: 0 0 auto;
}
.brand-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  flex: 0 0 auto;
  color: #fff;
  background: var(--brand, #176c56);
  border-radius: 11px;
}
.brand-name {
  font-size: 1.1rem;
  font-weight: 850;
  letter-spacing: -0.055em;
  line-height: 1.15;
}
.brand-name small {
  display: block;
  margin-top: 6px;
  font-size: 0.62rem;
  color: var(--muted, #64736a);
  font-weight: 550;
  letter-spacing: 0;
}
.desktop-heading {
  display: none;
}
.header-controls {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  min-width: 0;
}
.person-select {
  width: 142px;
  min-width: 0;
}
.person-caption {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin: 0 1px 4px;
  gap: 4px;
}
.person-caption label {
  display: block;
  font-size: 0.59rem;
  line-height: 1.2;
  font-weight: 650;
  min-width: 0;
  white-space: normal;
  overflow-wrap: anywhere;
  color: var(--muted, #64736a);
}
.person-caption span {
  font-size: 0.53rem;
  line-height: 1.3;
  color: var(--brand, #176c56);
  background: #e3eddf;
  padding: 2px 4px;
  border-radius: 4px;
  white-space: nowrap;
}
.person-select select {
  display: block;
  min-height: 44px;
  padding: 8px 20px 8px 10px;
  border-radius: 11px;
  border: 1px solid var(--line, #dbe2d8);
  background-color: var(--surface, #fff);
  font-size: 0.9rem;
  font-weight: 650;
  color: var(--ink, #142d25);
}
.shell-main,
.shell-status,
.shell-footer {
  width: 100%;
  max-width: 1200px;
  margin: 0 auto;
}
.shell-main {
  flex: 1;
  min-width: 0;
  padding: 24px 16px 32px;
}
.shell-status {
  padding: 0 16px;
}
.shell-footer {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  padding: 0 16px 20px;
  color: var(--muted, #64736a);
  font-size: 0.66rem;
}
.shell-state {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  justify-content: center;
  min-height: min(58dvh, 520px);
  max-width: 540px;
  padding: 20px 4px;
}
.shell-state h1 {
  margin: 24px 0 12px;
  font-size: clamp(1.8rem, 5vw, 2.6rem);
  letter-spacing: -0.055em;
}
.shell-state p {
  color: var(--muted, #64736a);
}
.state-symbol {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 68px;
  height: 68px;
  color: var(--brand, #176c56);
  border-radius: 22px;
  background: var(--accent, #ddf8a3);
}
.shell-state .state-hint {
  font-size: 0.78rem;
}
.connection-retry {
  margin-bottom: 24px;
}
.skip-link {
  position: fixed;
  z-index: 1000;
  left: 16px;
  top: 8px;
  transform: translateY(-160%);
  padding: 10px 16px;
  color: #fff;
  background: var(--brand, #176c56);
  border-radius: 10px;
  font-weight: 700;
}
.skip-link:focus {
  transform: translateY(0);
}
@media (max-width: 359px) {
  .shell-header {
    gap: 8px;
    padding-inline: 12px;
  }
  .brand-mark {
    width: 32px;
    height: 32px;
  }
  .brand-name {
    font-size: 1rem;
  }
  .person-select {
    width: 122px;
  }
  .person-caption span {
    display: none;
  }
  .person-select select {
    padding-left: 8px;
    padding-right: 14px;
  }
}
@media (min-width: 640px) {
  .shell-header {
    padding-inline: 28px;
  }
  .shell-main {
    padding: 36px 28px 44px;
  }
  .shell-status {
    padding-inline: 28px;
  }
  .shell-footer {
    padding-inline: 28px;
    font-size: 0.72rem;
  }
  .person-select {
    width: 180px;
  }
  .person-caption label {
    font-size: 0.65rem;
  }
}
@media (min-width: 1024px) {
  .shell-sidebar {
    position: fixed;
    inset: 0 auto 0 0;
    z-index: 40;
    display: flex;
    flex-direction: column;
    width: 232px;
    overflow-y: auto;
    padding: 30px 20px;
    border-right: 1px solid var(--line, #e4e8e1);
    background: var(--surface, #fff);
  }
  .shell-column {
    margin-left: 232px;
    padding-bottom: 0;
  }
  .sidebar-brand {
    margin: 0 0 48px 8px;
    gap: 10px;
  }
  .sidebar-brand .brand-mark {
    width: 38px;
    height: 38px;
    border-radius: 12px;
  }
  .sidebar-brand .brand-name {
    font-size: 1.35rem;
  }
  .sidebar-caption {
    margin: 0 14px 14px;
    color: var(--muted, #64736a);
    font-size: 0.6rem;
    font-weight: 750;
    letter-spacing: 0.16em;
    text-transform: uppercase;
  }
  .sidebar-offer {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    min-height: 48px;
    margin-top: 28px;
    padding: 12px 10px;
    border-radius: 14px;
    background: var(--accent, #ddf8a3);
    color: var(--ink, #142d25);
    font-size: 0.78rem;
    font-weight: 800;
    text-decoration: none;
  }
  .sidebar-offer:hover {
    background: #d2ef91;
  }
  .sidebar-bottom {
    margin-top: auto;
    padding: 28px 14px 0;
  }
  .sidebar-note-mark {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 38px;
    height: 38px;
    color: var(--brand, #176c56);
    background: var(--paper, #f8f8f2);
    border-radius: 12px;
    margin-bottom: 12px;
  }
  .sidebar-bottom p {
    margin: 0;
    font-size: 0.72rem;
    color: var(--muted, #64736a);
    line-height: 1.65;
  }
  .sidebar-bottom strong {
    color: var(--ink, #142d25);
    font-weight: 650;
  }
  .mobile-brand {
    display: none;
  }
  .desktop-heading {
    display: grid;
    gap: 4px;
  }
  .desktop-heading span {
    font-size: 0.68rem;
    color: var(--muted, #64736a);
  }
  .desktop-heading strong {
    font-size: 0.85rem;
    color: var(--ink, #142d25);
    font-weight: 650;
  }
  .shell-header {
    min-height: 100px;
    padding-inline: 40px;
    background: transparent;
  }
  .shell-main {
    padding: 40px;
  }
  .shell-status,
  .shell-footer {
    padding-inline: 40px;
  }
}
</style>
