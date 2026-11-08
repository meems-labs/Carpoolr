<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import type {
  CostOverviewTotals,
  GetPendingActionsResponse,
  Trip,
} from '../api/generated/trips/v1/trips_pb'
import AppIcon from '../components/AppIcon.vue'
import TripCard from '../components/TripCard.vue'
import { errorMessage } from '../composables/useOperation'
import { formatCents } from '../lib/labels'
import { beginOperation } from '../state/activity'
import { authenticationMode, currentProfile, loginUrl, selectedPersonId } from '../state/session'

interface SectionState {
  pending: boolean
  error: string
}

const personId = selectedPersonId.value
const client = personId ? clientsForPerson(personId) : undefined
const controller = new AbortController()
const nextState = reactive<SectionState>({ pending: !!personId, error: '' })
const taskState = reactive<SectionState>({ pending: !!personId, error: '' })
const costState = reactive<SectionState>({ pending: !!personId, error: '' })
const nextTrip = ref<Trip>()
const tasks = ref<GetPendingActionsResponse>()
const costTotals = ref<CostOverviewTotals>()
const hasBills = ref(false)
const today = new Date()
const dateLabel = new Intl.DateTimeFormat('de-DE', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  timeZone: 'Europe/Berlin',
}).format(today)
const taskCount = computed(
  () => (tasks.value?.bookingRequestCount ?? 0n) + (tasks.value?.paymentReportCount ?? 0n),
)
const ownAmountToPay = computed(() =>
  costTotals.value
    ? costTotals.value.ownUnpaidCents + costTotals.value.ownRejectedCents
    : undefined,
)

async function loadSection<T>(
  state: SectionState,
  request: (
    api: ReturnType<typeof clientsForPerson>,
    options: { signal: AbortSignal; timeoutMs: number },
  ) => Promise<T>,
  accept: (result: T) => void,
) {
  if (!client || controller.signal.aborted) return
  state.pending = true
  state.error = ''
  const finish = beginOperation()
  try {
    const result = await request(client, { signal: controller.signal, timeoutMs: 10_000 })
    if (!controller.signal.aborted && selectedPersonId.value === personId) accept(result)
  } catch (cause) {
    if (!controller.signal.aborted && selectedPersonId.value === personId)
      state.error = errorMessage(cause)
  } finally {
    if (!controller.signal.aborted) state.pending = false
    finish()
  }
}

function loadNextTrip() {
  return loadSection(
    nextState,
    (api, options) => api.dashboard.getNextTrip({}, options),
    (result) => (nextTrip.value = result.trip),
  )
}

function loadTasks() {
  return loadSection(
    taskState,
    (api, options) => api.dashboard.getPendingActions({}, options),
    (result) => (tasks.value = result),
  )
}

function loadCosts() {
  return loadSection(
    costState,
    (api, options) => api.billing.getMyCostOverview({ offset: 0, pageSize: 1 }, options),
    (result) => {
      if (!result.totals)
        throw new Error('Die Kostenübersicht konnte nicht vollständig geladen werden.')
      costTotals.value = result.totals
      hasBills.value = result.bills.length > 0
    },
  )
}

function choosePerson() {
  const picker = document.querySelector<HTMLSelectElement>('[data-person-select]')
  picker?.scrollIntoView({ block: 'center', behavior: 'smooth' })
  picker?.focus({ preventScroll: true })
}

onMounted(() => {
  if (personId) void Promise.allSettled([loadNextTrip(), loadTasks(), loadCosts()])
})
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <div class="home">
    <header class="home-hero">
      <div class="home-intro">
        <p v-if="personId" class="home-eyebrow">{{ dateLabel }}</p>
        <h1>{{ personId ? 'Übersicht' : 'Fahrgemeinschaften' }}</h1>

        <div v-if="personId" class="home-hero-actions">
          <RouterLink class="button" to="/fahrten">
            <AppIcon name="search" :size="19" />
            Fahrt suchen
          </RouterLink>
          <RouterLink class="button secondary" to="/fahrten/neu">
            <AppIcon name="plus" :size="19" />
            Fahrt anbieten
          </RouterLink>
        </div>
        <div v-else-if="authenticationMode === 'demo'" class="home-hero-actions">
          <button type="button" @click="choosePerson">
            Person auswählen
            <AppIcon name="arrow-right" :size="19" />
          </button>
          <span class="home-test-note">Testmodus ohne Anmeldung.</span>
        </div>
        <div v-else-if="authenticationMode === 'oidc'" class="home-hero-actions">
          <a class="button" :href="loginUrl('/')">Mit Authentik anmelden</a>
        </div>
        <p v-else class="muted">
          Die Browser-Anmeldung ist auf diesem Server noch nicht eingerichtet.
        </p>
      </div>
      <svg class="home-route-art" viewBox="0 0 280 280" fill="none" aria-hidden="true">
        <circle cx="145" cy="135" r="105" class="route-orbit" />
        <path d="M60 207V104a39 39 0 0 1 78 0v65a39 39 0 0 0 78 0V70" class="route-line" />
        <circle cx="60" cy="207" r="21" class="route-start" />
        <circle cx="60" cy="207" r="6" class="route-dot" />
        <circle cx="216" cy="70" r="21" class="route-end" />
        <path d="m208 70 6 6 11-12" class="route-check" />
        <path d="m177 208 8-12m-1 16 12-6" class="route-spark" />
      </svg>
    </header>

    <template v-if="personId">
      <aside v-if="!currentProfile?.isSaved" class="home-profile-note">
        <AppIcon name="user" :size="23" />
        <p>
          <strong>Profil vervollständigen</strong>
          Ergänze deine Abholadresse, um einen Platz anzufragen, und dein Auto, wenn du selbst
          fährst.
        </p>
        <RouterLink to="/profil">
          Profil ergänzen
          <AppIcon name="arrow-right" :size="18" />
        </RouterLink>
      </aside>

      <div class="home-overview">
        <section class="home-next" aria-labelledby="home-next-heading">
          <div class="home-section-heading">
            <div>
              <h2 id="home-next-heading">Deine nächste Fahrt</h2>
            </div>
            <AppIcon name="calendar" :size="24" />
          </div>
          <p v-if="nextState.pending" class="home-section-status" role="status">
            Deine nächste Fahrt wird geladen …
          </p>
          <div v-else-if="nextState.error" class="home-section-status" role="alert">
            <p>{{ nextState.error }}</p>
            <button type="button" class="secondary" @click="loadNextTrip">Erneut versuchen</button>
          </div>
          <TripCard v-else-if="nextTrip" :trip="nextTrip" featured />
          <div v-else class="home-next-empty">
            <AppIcon name="car" :size="35" />
            <h3>Keine kommende Fahrt</h3>
            <p>Deine nächste angebotene oder zugesagte Mitfahrt erscheint hier.</p>
            <RouterLink to="/fahrten">
              Fahrt finden
              <AppIcon name="arrow-right" :size="18" />
            </RouterLink>
          </div>
          <div class="home-personal-links">
            <RouterLink to="/meine-fahrten">Meine Angebote</RouterLink>
            <RouterLink to="/meine-anfragen">Meine Mitfahrten</RouterLink>
          </div>
        </section>

        <section class="home-tasks" aria-labelledby="home-tasks-heading">
          <div class="home-section-heading">
            <h2 id="home-tasks-heading">Offene Aufgaben</h2>
            <span v-if="tasks && !taskState.pending && !taskState.error" class="home-task-total">
              {{ taskCount.toLocaleString('de-DE') }} offen
            </span>
          </div>
          <p v-if="taskState.pending" class="home-task-status" role="status">
            Offene Aufgaben werden geladen …
          </p>
          <div v-else-if="taskState.error" class="home-task-status" role="alert">
            <p>{{ taskState.error }}</p>
            <button type="button" class="secondary" @click="loadTasks">Erneut versuchen</button>
          </div>
          <template v-else-if="tasks">
            <div v-if="taskCount === 0n" class="home-task-empty">
              <AppIcon name="check" :size="30" />
              <h3>Keine offenen Aufgaben</h3>
              <p>Neue Mitfahranfragen und gemeldete Zahlungen erscheinen hier.</p>
            </div>
            <div v-else class="home-task-list">
              <RouterLink v-if="tasks.bookingRequestCount > 0n" to="/eingegangene-anfragen">
                <span class="home-task-number">
                  {{ tasks.bookingRequestCount.toLocaleString('de-DE') }}
                </span>
                <span>
                  <strong>
                    {{ tasks.bookingRequestCount === 1n ? 'Mitfahranfrage' : 'Mitfahranfragen' }}
                  </strong>
                  <small>Warten auf deine Entscheidung</small>
                </span>
                <AppIcon name="arrow-right" :size="20" />
              </RouterLink>
              <RouterLink v-if="tasks.paymentReportCount > 0n" to="/kosten">
                <span class="home-task-number">
                  {{ tasks.paymentReportCount.toLocaleString('de-DE') }}
                </span>
                <span>
                  <strong>
                    {{
                      tasks.paymentReportCount === 1n ? 'Gemeldete Zahlung' : 'Gemeldete Zahlungen'
                    }}
                  </strong>
                  <small>Prüfen und bestätigen</small>
                </span>
                <AppIcon name="arrow-right" :size="20" />
              </RouterLink>
            </div>
          </template>
        </section>
      </div>

      <section class="home-costs" aria-labelledby="home-cost-heading">
        <div class="home-cost-intro">
          <h2 id="home-cost-heading">Kostenübersicht</h2>
          <p>Deine Anteile und Erstattungen aus abgeschlossenen Fahrten.</p>
          <RouterLink to="/kosten">
            Zur Kostenübersicht
            <AppIcon name="arrow-right" :size="18" />
          </RouterLink>
        </div>
        <div class="home-cost-values">
          <p v-if="costState.pending" role="status">Kostenübersicht wird geladen …</p>
          <div v-else-if="costState.error" role="alert">
            <p>{{ costState.error }}</p>
            <button type="button" class="secondary" @click="loadCosts">Erneut versuchen</button>
          </div>
          <p v-else-if="!hasBills" class="home-cost-empty">
            Noch keine abgerechneten Fahrten. Sobald eine Fahrt abgeschlossen ist, findest du hier
            deinen Kostenüberblick.
          </p>
          <dl v-else-if="costTotals">
            <div>
              <dt>Dein Kostenanteil gesamt</dt>
              <dd>{{ formatCents(costTotals.ownCostCents) }}</dd>
            </div>
            <div>
              <dt>Davon noch zu zahlen</dt>
              <dd>{{ formatCents(ownAmountToPay) }}</dd>
            </div>
            <div v-if="costTotals.driverTotalReceivableCents > 0n">
              <dt>Ausstehende Erstattungen an dich</dt>
              <dd>{{ formatCents(costTotals.driverOutstandingCents) }}</dd>
            </div>
          </dl>
        </div>
      </section>
    </template>

    <section v-else class="home-how" aria-labelledby="home-how-heading">
      <div class="home-section-heading">
        <h2 id="home-how-heading">Fahrgemeinschaften nutzen</h2>
      </div>
      <ol>
        <li>
          <span class="home-step-number">01</span>
          <AppIcon name="user" :size="26" />
          <h3>Profil ergänzen</h3>
          <p>Name, Abholadresse und optional dein Auto hinterlegen.</p>
        </li>
        <li>
          <span class="home-step-number">02</span>
          <AppIcon name="car" :size="26" />
          <h3>Fahrt anbieten oder anfragen</h3>
          <p>
            Biete eine Fahrt an oder frage einen freien Platz an. Der Fahrer bestätigt die Mitfahrt.
          </p>
        </li>
        <li>
          <span class="home-step-number">03</span>
          <AppIcon name="wallet" :size="26" />
          <h3>Kosten teilen</h3>
          <p>
            Nach der Fahrt werden die Kosten aufgeteilt. Zahlungen meldet und bestätigt ihr selbst.
          </p>
        </li>
      </ol>
    </section>
  </div>
</template>

<style scoped>
.home {
  padding-bottom: 0.5rem;
  overflow-wrap: anywhere;
}
.home-hero {
  display: grid;
  align-items: center;
  gap: 2rem;
  padding-block: 1.25rem 2.5rem;
}
.home-eyebrow {
  margin: 0 0 0.85rem;
  font-size: 0.75rem;
  font-weight: 750;
  letter-spacing: 0.055em;
  color: var(--brand, #176c56);
}
.home-hero h1 {
  margin: 0;
  font-size: clamp(2.2rem, 6vw, 4.45rem);
  font-weight: 750;
  letter-spacing: -0.065em;
  line-height: 1.08;
  color: var(--ink, #142d25);
}
.home-hero-actions {
  margin-top: 1.25rem;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.65rem;
}
.home-test-note {
  font-size: 0.8rem;
  color: var(--muted, #68736a);
}
.home-route-art {
  display: none;
  width: 100%;
  max-width: 280px;
  justify-self: end;
}
.route-orbit {
  stroke: var(--line, #dedfd6);
  stroke-width: 1.5;
  stroke-dasharray: 4 8;
}
.route-line {
  stroke: var(--brand, #176c56);
  stroke-width: 9;
  stroke-linecap: round;
}
.route-start {
  fill: var(--accent, #ddf8a3);
  stroke: var(--paper, #f5f4ef);
  stroke-width: 8;
}
.route-dot,
.route-end {
  fill: var(--brand, #176c56);
}
.route-check {
  stroke: var(--accent, #ddf8a3);
  stroke-width: 3;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.route-spark {
  stroke: var(--brand, #176c56);
  stroke-width: 2;
  stroke-linecap: round;
}
.home-profile-note {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.8rem;
  margin-bottom: 2rem;
  padding: 1rem 1.15rem;
  border: 1px solid var(--line, #dedfd6);
  border-radius: 1rem;
  background: var(--surface, #fff);
  color: var(--brand, #176c56);
}
.home-profile-note p {
  flex: 1 1 15rem;
  margin: 0;
  font-size: 0.85rem;
  color: var(--muted, #68736a);
}
.home-profile-note strong {
  display: block;
  color: var(--ink, #142d25);
}
.home-profile-note a,
.home-next-empty a,
.home-cost-intro a {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  min-height: 44px;
  font-size: 0.85rem;
  font-weight: 750;
  text-decoration: none;
}
.home-overview {
  display: grid;
  gap: 2rem;
}
.home-next,
.home-tasks {
  min-width: 0;
}
.home-section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}
.home-section-heading .home-eyebrow {
  margin-bottom: 0.4rem;
}
.home-section-heading h2 {
  margin: 0;
  font-size: 1.35rem;
  letter-spacing: -0.035em;
}
.home-section-heading > svg {
  flex-shrink: 0;
  color: var(--brand, #176c56);
}
.home-section-status,
.home-next-empty {
  padding: 1.65rem;
  border: 1px solid var(--line, #dedfd6);
  border-radius: 1rem;
  background: var(--surface, #fff);
}
.home-next-empty > svg {
  margin-bottom: 1rem;
  color: var(--brand, #176c56);
}
.home-next-empty h3 {
  margin-bottom: 0.5rem;
  font-size: 1.15rem;
}
.home-next-empty p,
.home-section-status {
  font-size: 0.9rem;
  color: var(--muted, #68736a);
}
.home-next-empty p {
  max-width: 26rem;
  margin-bottom: 0.45rem;
}
.home-personal-links {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem 1.3rem;
  margin-top: 0.65rem;
}
.home-personal-links a {
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  font-size: 0.8rem;
  color: var(--muted, #68736a);
}
.home-tasks {
  padding: 1.4rem;
  border-radius: 1.25rem;
  background: var(--emphasis-surface);
  color: var(--emphasis-ink);
  align-self: start;
}
.home-tasks h2,
.home-tasks h3 {
  color: inherit;
}
.home-tasks button.secondary {
  color: var(--emphasis-ink);
  border-color: var(--emphasis-control-border);
  background: var(--emphasis-control);
}
.home-tasks button.secondary:hover {
  background: var(--emphasis-control-hover);
}
.home-tasks :focus-visible {
  outline-color: var(--accent);
}
.home-task-total {
  flex-shrink: 0;
  padding: 0.25rem 0.55rem;
  border: 1px solid var(--emphasis-line);
  border-radius: 999px;
  font-size: 0.7rem;
  color: var(--accent, #ddf8a3);
}
.home-task-list a {
  display: flex;
  align-items: center;
  gap: 0.9rem;
  min-height: 92px;
  padding-block: 0.85rem;
  border-top: 1px solid var(--emphasis-line);
  color: inherit;
  text-decoration: none;
}
.home-task-list a:hover strong {
  text-decoration: underline;
  text-underline-offset: 0.25em;
}
.home-task-list a > svg {
  margin-left: auto;
  flex-shrink: 0;
  color: var(--accent, #ddf8a3);
}
.home-task-number {
  flex: 0 1 auto;
  max-width: 40%;
  font-size: 2.2rem;
  font-weight: 650;
  letter-spacing: -0.05em;
  color: var(--accent, #ddf8a3);
}
.home-task-list strong,
.home-task-list small {
  display: block;
}
.home-task-list strong {
  font-size: 0.88rem;
}
.home-task-list small {
  margin-top: 0.2rem;
  font-size: 0.75rem;
  color: var(--emphasis-muted);
}
.home-task-empty,
.home-task-status {
  padding-block: 0.65rem 0.25rem;
  font-size: 0.88rem;
  color: var(--emphasis-muted);
}
.home-task-empty > svg {
  color: var(--accent, #ddf8a3);
  margin-bottom: 0.9rem;
}
.home-task-empty h3 {
  margin-bottom: 0.5rem;
  font-size: 1.15rem;
}
.home-task-empty p {
  margin-bottom: 0;
}
.home-costs {
  display: grid;
  gap: 1.25rem;
  margin-top: 2.75rem;
  padding-block: 2rem 0.5rem;
  border-top: 1px solid var(--line, #dedfd6);
}
.home-cost-intro h2 {
  margin-bottom: 0.7rem;
  font-size: 1.6rem;
}
.home-cost-intro > p:not(.home-eyebrow),
.home-cost-values {
  font-size: 0.88rem;
  color: var(--muted, #68736a);
}
.home-cost-intro > p:not(.home-eyebrow) {
  max-width: 23rem;
  margin-bottom: 0.45rem;
}
.home-cost-values dl {
  margin: 0;
}
.home-cost-values dl > div {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  padding-block: 0.85rem;
  border-bottom: 1px solid var(--line, #dedfd6);
}
.home-cost-values dl > div:first-child {
  padding-top: 0;
}
.home-cost-values dd {
  flex-shrink: 1;
  min-width: 0;
  max-width: 55%;
  overflow-wrap: anywhere;
  text-align: right;
  margin: 0;
  color: var(--ink, #142d25);
  font-size: 1.1rem;
  font-weight: 750;
  letter-spacing: -0.035em;
}
.home-cost-values dt {
  max-width: 17rem;
}
.home-cost-empty {
  max-width: 28rem;
  padding-block: 0.5rem;
}
.home-how {
  padding-block: 1.5rem 0;
  border-top: 1px solid var(--line, #dedfd6);
}
.home-how > .home-section-heading {
  display: block;
  margin-bottom: 1.75rem;
}
.home-how h2 {
  max-width: 23rem;
  font-size: 1.55rem;
}
.home-how ol {
  display: grid;
  gap: 1.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}
.home-how li {
  position: relative;
  padding-top: 0.8rem;
  border-top: 1px solid var(--line, #dedfd6);
}
.home-step-number {
  display: block;
  margin-bottom: 1.15rem;
  color: var(--muted, #68736a);
  font-size: 0.7rem;
  font-weight: 650;
}
.home-how li > svg {
  position: absolute;
  top: 0.65rem;
  right: 0;
  color: var(--brand, #176c56);
}
.home-how h3 {
  margin-bottom: 0.55rem;
  font-size: 1rem;
}
.home-how li p {
  max-width: 23rem;
  margin-bottom: 0;
  color: var(--muted, #68736a);
  font-size: 0.88rem;
  line-height: 1.65;
}
@media (min-width: 640px) {
  .home-hero {
    padding-block: 2.3rem 3rem;
  }
  .home-how ol {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 2rem;
  }
  .home-costs {
    grid-template-columns: 1fr 1.15fr;
    gap: 2.5rem;
  }
}
@media (min-width: 900px) {
  .home-hero {
    grid-template-columns: minmax(0, 1fr) 230px;
    gap: 1rem;
    padding-bottom: 3.5rem;
  }
  .home-route-art {
    display: block;
  }
  .home-overview {
    grid-template-columns: minmax(0, 1.35fr) minmax(0, 1fr);
    gap: 2rem;
  }
  .home-tasks {
    margin-top: 0.1rem;
    padding: 1.6rem;
  }
}
</style>
