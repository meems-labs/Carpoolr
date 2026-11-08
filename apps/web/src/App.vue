<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { clientsForPerson } from './api/application'
import AppIcon from './components/AppIcon.vue'
import AppNavigation from './components/AppNavigation.vue'
import OperationStatus from './components/OperationStatus.vue'
import NotificationBell from './components/NotificationBell.vue'
import PersonalTripsNav from './components/PersonalTripsNav.vue'
import PwaControls from './components/PwaControls.vue'
import ThemeControl from './components/ThemeControl.vue'
import { updateApplying } from './pwa/client'
import { errorMessage } from './composables/useOperation'
import { hasPersonalTripsNavigation } from './lib/navigation'
import { activeOperationCount, beginOperation, dirtyFormCount } from './state/activity'
import {
  authenticationMode,
  currentProfile,
  initializeSession,
  loginUrl,
  logout,
  people,
  selectedPersonId,
  selectPerson,
  sessionExpired,
  signedInUser,
} from './state/session'

const loading = ref(true)
const error = ref('')
const profileLoading = ref(false)
const signingOut = ref(false)
const authenticationError = ref('')
const route = useRoute()
const router = useRouter()
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
    await initializeSession(callController.signal)
    if (authenticationMode.value === 'oidc' && route.query.authError) {
      authenticationError.value =
        route.query.authError === 'access_denied'
          ? 'Dein Konto ist für Carpoolr nicht freigeschaltet.'
          : 'Die Anmeldung ist fehlgeschlagen. Bitte erneut versuchen.'
      const query = { ...route.query }
      delete query.authError
      await router.replace({ path: route.path, query, hash: route.hash })
    }
    if (selectedPersonId.value && !callController.signal.aborted) await refreshProfile()
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

watch(selectedPersonId, () => {
  if (!loading.value) void refreshProfile()
})
onMounted(initialize)
onBeforeUnmount(() => {
  controller.abort()
  profileController.abort()
})

function select(event: Event) {
  selectPerson((event.target as HTMLSelectElement).value)
}

function canLeaveSession(): boolean {
  if (activeOperationCount.value > 0) {
    error.value = 'Bitte warte, bis die laufende Aktion abgeschlossen ist.'
    return false
  }
  return dirtyFormCount.value === 0 || window.confirm('Ungespeicherte Änderungen verwerfen?')
}

function signIn() {
  if (!canLeaveSession()) return
  authenticationError.value = ''
  const finish = beginOperation()
  try {
    window.location.assign(loginUrl(route.fullPath))
    // Hold the PWA update gate during navigation; release if the browser stays on this page.
    window.setTimeout(finish, 15_000)
  } catch (cause) {
    finish()
    error.value = errorMessage(cause)
  }
}

async function signOut() {
  if (signingOut.value || !canLeaveSession()) return
  const finish = beginOperation()
  signingOut.value = true
  error.value = ''
  try {
    await logout(controller.signal)
    await router.replace('/')
  } catch (cause) {
    if (!controller.signal.aborted) error.value = errorMessage(cause)
  } finally {
    signingOut.value = false
    finish()
  }
}
</script>

<template>
  <a class="skip-link" href="#content">Zum Inhalt</a>
  <div class="app-shell">
    <aside class="shell-sidebar" data-pwa-update-lock :inert="updateApplying">
      <RouterLink class="shell-brand sidebar-brand" to="/" aria-label="Carpoolr – Startseite">
        <span class="brand-mark"><AppIcon name="car" :size="26" /></span>
        <span class="brand-name">Carpoolr</span>
      </RouterLink>
      <AppNavigation variant="desktop" />
      <RouterLink class="sidebar-offer" to="/fahrten/neu">
        <AppIcon name="plus" :size="19" />
        Fahrt anbieten
      </RouterLink>
    </aside>

    <div class="shell-column">
      <header class="shell-header" data-pwa-update-lock :inert="updateApplying">
        <RouterLink class="shell-brand mobile-brand" to="/" aria-label="Carpoolr – Startseite">
          <span class="brand-mark"><AppIcon name="car" :size="23" /></span>
          <span class="brand-name">Carpoolr</span>
        </RouterLink>
        <div class="header-controls">
          <div v-if="authenticationMode === 'demo'" class="person-select">
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
          <div v-else-if="authenticationMode === 'oidc'" class="account-controls">
            <template v-if="selectedPersonId">
              <RouterLink class="account-name" to="/profil">
                {{ currentProfile?.displayName || signedInUser?.displayName || 'Mein Konto' }}
              </RouterLink>
              <button type="button" class="secondary" :disabled="signingOut" @click="signOut">
                {{ signingOut ? 'Wird abgemeldet …' : 'Abmelden' }}
              </button>
            </template>
            <button v-else type="button" :disabled="loading" @click="signIn">Anmelden</button>
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
        <OperationStatus :error="authenticationError" />
        <div v-if="sessionExpired" class="notice" role="status">
          <p>
            Deine Sitzung ist abgelaufen oder hat sich geändert. Melde dich erneut an, um
            fortzufahren. Ungespeicherte Eingaben bleiben bis dahin sichtbar.
          </p>
          <button type="button" @click="signIn">Erneut anmelden</button>
        </div>
        <section v-if="loading || profileLoading" class="shell-state" role="status">
          <span class="state-symbol"><AppIcon name="car" :size="28" /></span>
          <h1>Wird geladen …</h1>
          <p>Deine Fahrgemeinschaften werden geladen …</p>
        </section>
        <section v-else-if="error && !currentProfile" class="shell-state">
          <span class="state-symbol"><AppIcon name="arrow-right" :size="26" /></span>
          <h1>Ansicht konnte nicht geladen werden</h1>
          <button @click="selectedPersonId ? refreshProfile() : initialize()">
            Erneut versuchen
          </button>
        </section>
        <template v-else-if="publicHome">
          <RouterView :key="`${selectedPersonId}:${route.path}`" />
        </template>
        <section v-else-if="!selectedPersonId && authenticationMode === 'oidc'" class="shell-state">
          <h1>Anmelden</h1>
          <p>Melde dich über Authentik an, um Fahrten und Kosten zu öffnen.</p>
          <button type="button" @click="signIn">Mit Authentik anmelden</button>
        </section>
        <section
          v-else-if="!selectedPersonId && authenticationMode === 'bearer'"
          class="shell-state"
        >
          <h1>Anmeldung noch nicht eingerichtet</h1>
          <p>Für diesen Server ist noch keine Browser-Anmeldung konfiguriert.</p>
        </section>
        <section v-else-if="!selectedPersonId" class="shell-state">
          <span class="state-symbol"><AppIcon name="users" :size="28" /></span>
          <h1>Person auswählen</h1>
          <p>Wähle oben eine Testperson aus, um ihre Fahrten und ihr Profil zu öffnen.</p>
          <p class="state-hint">Im Testmodus bestimmt die Auswahl, wessen Daten du bearbeitest.</p>
        </section>
        <template v-else>
          <PersonalTripsNav v-if="hasPersonalTripsNavigation(route.path)" />
          <RouterView :key="`${selectedPersonId}:${route.path}`" />
        </template>
      </main>
      <footer class="shell-footer">
        <ThemeControl />
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
  flex-wrap: wrap;
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
.account-controls {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 0.5rem;
  min-width: 0;
}
.account-name {
  max-width: 12rem;
  overflow-wrap: anywhere;
  font-size: 0.85rem;
  font-weight: 750;
}
.account-controls button {
  padding-inline: 0.75rem;
}
.brand-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  flex: 0 0 auto;
  color: var(--on-brand);
  background: var(--brand, #176c56);
  border-radius: 11px;
}
.brand-name {
  font-size: 1.1rem;
  font-weight: 850;
  letter-spacing: -0.055em;
  line-height: 1.15;
}
.header-controls {
  margin-left: auto;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  flex-shrink: 0;
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
  background: var(--selected-surface);
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
  align-items: center;
  flex-wrap: wrap;
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
  color: var(--on-accent);
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
  color: var(--on-brand);
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
    color: var(--on-accent);
    font-size: 0.78rem;
    font-weight: 800;
    text-decoration: none;
  }
  .sidebar-offer:hover {
    background: var(--accent-hover);
  }
  .mobile-brand {
    display: none;
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
