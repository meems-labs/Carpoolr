<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { clientsForPerson } from '../api/application'
import {
  PendingActionKind,
  type GetPendingActionsResponse,
} from '../api/generated/trips/v1/trips_pb'
import { errorMessage } from '../composables/useOperation'
import { formatDate } from '../lib/format'
import { formatCents } from '../lib/labels'
import { dashboardRefreshVersion } from '../state/dashboard'
import { selectedPersonId } from '../state/session'

const route = useRoute()
const root = ref<HTMLElement>()
const trigger = ref<HTMLButtonElement>()
const open = ref(false)
const pending = ref(false)
const error = ref('')
const data = ref<GetPendingActionsResponse>()
let controller: AbortController | undefined
let timer: ReturnType<typeof setInterval> | undefined
const total = computed(
  () => (data.value?.bookingRequestCount ?? 0n) + (data.value?.paymentReportCount ?? 0n),
)
const label = computed(() =>
  error.value
    ? 'Offene Aufgaben: Aktualisierung fehlgeschlagen'
    : data.value
      ? `Offene Aufgaben: ${total.value}`
      : 'Offene Aufgaben laden',
)

async function refresh() {
  const personId = selectedPersonId.value
  if (!personId) return
  controller?.abort()
  const call = new AbortController()
  controller = call
  pending.value = true
  try {
    const response = await clientsForPerson(personId).dashboard.getPendingActions(
      {},
      { signal: call.signal, timeoutMs: 10_000 },
    )
    if (controller === call && selectedPersonId.value === personId && !call.signal.aborted) {
      data.value = response
      error.value = ''
    }
  } catch (cause) {
    if (controller === call && !call.signal.aborted) error.value = errorMessage(cause)
  } finally {
    if (controller === call) pending.value = false
  }
}

function toggle() {
  open.value = !open.value
  if (open.value) void refresh()
}
function close() {
  open.value = false
}
function outside(event: PointerEvent) {
  if (event.target instanceof Node && !root.value?.contains(event.target)) close()
}
function escape(event: KeyboardEvent) {
  if (event.key === 'Escape' && open.value) {
    close()
    trigger.value?.focus()
  }
}
function visibleRefresh() {
  if (document.visibilityState === 'visible') void refresh()
}

watch(dashboardRefreshVersion, () => void refresh())
watch(
  () => route.fullPath,
  () => {
    close()
    void refresh()
  },
)
onMounted(() => {
  void refresh()
  timer = setInterval(visibleRefresh, 30_000)
  document.addEventListener('pointerdown', outside)
  document.addEventListener('keydown', escape)
  document.addEventListener('visibilitychange', visibleRefresh)
  window.addEventListener('focus', visibleRefresh)
})
onBeforeUnmount(() => {
  controller?.abort()
  if (timer) clearInterval(timer)
  document.removeEventListener('pointerdown', outside)
  document.removeEventListener('keydown', escape)
  document.removeEventListener('visibilitychange', visibleRefresh)
  window.removeEventListener('focus', visibleRefresh)
})
</script>

<template>
  <div ref="root" class="notifications">
    <button
      ref="trigger"
      class="secondary bell"
      type="button"
      :aria-label="label"
      :aria-expanded="open"
      aria-controls="pending-actions"
      @click="toggle"
    >
      <svg
        width="23"
        height="23"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.8"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" />
      </svg>
      <span v-if="error" class="count error-count" aria-hidden="true">!</span>
      <span v-else-if="total > 0n" class="count" aria-hidden="true">
        {{ total > 99n ? '99+' : total.toString() }}
      </span>
    </button>
    <section
      v-if="open"
      id="pending-actions"
      class="notification-panel"
      aria-label="Offene Aufgaben"
    >
      <div class="split">
        <h2>Offene Aufgaben</h2>
        <button class="secondary refresh" type="button" :disabled="pending" @click="refresh">
          Aktualisieren
        </button>
      </div>
      <p v-if="error" class="notice notice-error" role="alert">
        {{ error }} Vorhandene Angaben können veraltet sein.
      </p>
      <p v-if="pending && !data" role="status">Aufgaben werden geladen …</p>
      <template v-if="data">
        <div class="task-totals">
          <RouterLink to="/eingegangene-anfragen" @click="close">
            {{ data.bookingRequestCount.toString() }}
            {{
              data.bookingRequestCount === 1n ? 'offene Mitfahranfrage' : 'offene Mitfahranfragen'
            }}
          </RouterLink>
          <RouterLink to="/kosten" @click="close">
            {{ data.paymentReportCount.toString() }}
            {{ data.paymentReportCount === 1n ? 'Zahlung' : 'Zahlungen' }} bestätigen
          </RouterLink>
        </div>
        <p v-if="total === 0n" class="muted">Keine offenen Aufgaben.</p>
        <ul v-else class="task-list">
          <li
            v-for="action in data.actions"
            :key="`${action.kind}:${action.tripId}:${action.bookingId}:${action.profileId}`"
          >
            <RouterLink :to="`/fahrten/${action.tripId}`" @click="close">
              <strong>
                {{
                  action.kind === PendingActionKind.BOOKING_REQUEST
                    ? 'Mitfahranfrage'
                    : 'Zahlung gemeldet'
                }}
                · {{ action.displayName }}
              </strong>
              <span>{{ action.origin }} → {{ action.destination }}</span>
              <span class="small muted">
                {{ formatDate(action.departureAt) }}
                <template v-if="action.kind === PendingActionKind.PAYMENT_REPORT">
                  · {{ formatCents(action.amountCents) }}
                </template>
              </span>
            </RouterLink>
          </li>
        </ul>
        <p v-if="total > BigInt(data.actions.length)" class="small muted">
          Hier siehst du eine Auswahl. Alle Aufgaben findest du in den Übersichten oben.
        </p>
      </template>
    </section>
  </div>
</template>

<style scoped>
.notifications {
  position: relative;
  flex: 0 0 auto;
}
.bell {
  position: relative;
  width: 48px;
  height: 48px;
  padding: 0.6rem;
}
.count {
  position: absolute;
  top: -4px;
  right: -4px;
  min-width: 22px;
  padding: 2px 4px;
  border-radius: 1rem;
  background: var(--brand);
  color: var(--on-brand);
  font-size: 0.7rem;
  border: 2px solid var(--paper);
}
.error-count {
  background: var(--danger-ink);
  color: var(--danger-surface);
}
.notification-panel {
  position: absolute;
  top: calc(100% + 0.75rem);
  right: 0;
  z-index: 20;
  width: min(28rem, calc(100vw - 2rem));
  max-height: min(75vh, 42rem);
  overflow-y: auto;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 0.8rem;
  padding: 1.1rem;
  box-shadow: var(--popover-shadow);
}
.notification-panel h2 {
  margin: 0.3rem 0;
}
.refresh {
  min-height: 44px;
  font-size: 0.8rem;
  padding: 0.4rem 0.65rem;
}
.task-totals {
  display: grid;
  gap: 0.4rem;
  margin: 1rem 0;
}
.task-list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.task-list li {
  border-top: 1px solid var(--border);
}
.task-list a {
  display: grid;
  gap: 0.25rem;
  padding: 0.8rem 0;
  text-decoration: none;
  overflow-wrap: anywhere;
}
.task-list a:hover strong {
  text-decoration: underline;
}
.task-list strong {
  font-size: 0.9rem;
}
@media (max-width: 1023px) {
  .notification-panel {
    max-height: min(75dvh, calc(100dvh - 190px - env(safe-area-inset-bottom)));
  }
}
</style>
