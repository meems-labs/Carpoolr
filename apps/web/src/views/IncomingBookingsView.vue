<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import { BookingState, type BookingWithTrip } from '../api/generated/trips/v1/trips_pb'
import OperationStatus from '../components/OperationStatus.vue'
import { useOperation } from '../composables/useOperation'
import { formatDate } from '../lib/format'
import { bookingLabel } from '../lib/labels'
import { requestDashboardRefresh } from '../state/dashboard'
import { selectedPersonId } from '../state/session'

const client = clientsForPerson(selectedPersonId.value)
const { pending, error, success, run } = useOperation()
const entries = ref<BookingWithTrip[]>([])
const nextOffset = ref<number>()
const pendingOnly = ref(true)
const loaded = ref(false)
let displayedPendingOnly = true

async function load(append = false) {
  const filter = pendingOnly.value
  const response = await run((options) =>
    client.bookings.listIncomingBookings(
      {
        offset: append ? (nextOffset.value ?? 0) : 0,
        pageSize: 20,
        pendingOnly: filter,
      },
      options,
    ),
  )
  if (response) {
    if (append) {
      const merged = new Map(entries.value.map((entry) => [entry.booking?.id, entry]))
      for (const entry of response.entries) merged.set(entry.booking?.id, entry)
      entries.value = [...merged.values()]
    } else entries.value = response.entries
    nextOffset.value = response.nextOffset
    displayedPendingOnly = filter
    loaded.value = true
  } else pendingOnly.value = displayedPendingOnly
}

async function decide(bookingId: string, accept: boolean) {
  const response = await run(async (options) => {
    if (accept) await client.bookings.acceptBooking({ bookingId }, options)
    else await client.bookings.rejectBooking({ bookingId }, options)
    requestDashboardRefresh()
    return client.bookings.listIncomingBookings(
      { pageSize: 20, pendingOnly: pendingOnly.value },
      options,
    )
  })
  if (response) {
    entries.value = response.entries
    nextOffset.value = response.nextOffset
    loaded.value = true
    success.value = accept
      ? 'Die Mitfahranfrage wurde bestätigt.'
      : 'Die Mitfahranfrage wurde abgelehnt.'
  }
}

function showAll() {
  pendingOnly.value = false
  return load()
}
onMounted(() => load())
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Eingegangene Anfragen</h1>
      <p>Mitfahranfragen zu deinen eigenen Fahrtangeboten zentral bearbeiten.</p>
    </div>
    <button class="secondary" :disabled="pending" @click="load()">Aktualisieren</button>
  </header>
  <section class="card incoming-filter">
    <label class="checkbox">
      <input v-model="pendingOnly" type="checkbox" :disabled="pending" @change="load()" />
      Nur offene Anfragen anzeigen
    </label>
    <p class="hint">
      Offene Anfragen betreffen noch nicht abgefahrene, geplante Fahrten. Erst deine Zusage belegt
      einen Platz.
    </p>
  </section>
  <OperationStatus :error="error" :success="success" />
  <p v-if="pending && !loaded" role="status">Eingegangene Anfragen werden geladen …</p>
  <div v-if="error && !loaded" class="actions">
    <button class="secondary" :disabled="pending" @click="load()">Erneut versuchen</button>
  </div>
  <section v-if="loaded && !entries.length && !pending" class="card empty">
    <h2>{{ pendingOnly ? 'Keine offenen Anfragen' : 'Noch keine eingegangenen Anfragen' }}</h2>
    <p>
      {{
        pendingOnly
          ? 'Aktuell wartet keine Mitfahranfrage auf deine Entscheidung.'
          : 'Sobald jemand bei einer deiner Fahrten mitfahren möchte, erscheint die Anfrage hier.'
      }}
    </p>
    <button v-if="pendingOnly" class="secondary" @click="showAll">
      Auch bearbeitete Anfragen anzeigen
    </button>
    <RouterLink v-else class="button secondary" to="/meine-fahrten">Meine Angebote</RouterLink>
  </section>
  <div class="stack">
    <article v-for="entry in entries" :key="entry.booking?.id" class="card">
      <template v-if="entry.booking">
        <div class="split">
          <h2>{{ entry.booking.requesterDisplayName }}</h2>
          <span
            class="badge"
            :class="{
              warning: entry.booking.state === BookingState.PENDING,
              success: entry.booking.state === BookingState.ACCEPTED,
            }"
          >
            {{ bookingLabel(entry.booking.state) }}
          </span>
        </div>
        <p class="small muted">Angefragt am {{ formatDate(entry.booking.requestedAt) }}</p>
        <template v-if="entry.trip">
          <p class="incoming-departure">
            <strong>Abfahrt: {{ formatDate(entry.trip.departureAt) }}</strong>
          </p>
          <div class="trip-route">
            <p>
              <small class="route-label">Start</small>
              {{ entry.trip.origin }}
            </p>
            <span aria-hidden="true">→</span>
            <p>
              <small class="route-label">Ziel</small>
              {{ entry.trip.destination }}
            </p>
          </div>
        </template>
        <p v-if="entry.booking.pickupAddress" class="address incoming-pickup">
          <span class="route-label">Abholadresse</span>
          {{ entry.booking.pickupAddress }}
        </p>
        <p v-else-if="entry.booking.state === BookingState.PENDING" class="hint incoming-pickup">
          Die Abholadresse wird nach deiner Zusage sichtbar.
        </p>
        <div class="actions">
          <button
            v-if="entry.booking.canAccept"
            :disabled="pending"
            @click="decide(entry.booking.id, true)"
          >
            Zusagen
          </button>
          <button
            v-if="entry.booking.canReject"
            class="danger"
            :disabled="pending"
            @click="decide(entry.booking.id, false)"
          >
            Ablehnen
          </button>
          <RouterLink v-if="entry.trip" class="button secondary" :to="`/fahrten/${entry.trip.id}`">
            Fahrtdetails
          </RouterLink>
        </div>
      </template>
    </article>
  </div>
  <div v-if="nextOffset !== undefined" class="actions">
    <button class="secondary" :disabled="pending" @click="load(true)">
      {{ pending ? 'Wird geladen …' : 'Weitere Anfragen laden' }}
    </button>
  </div>
</template>

<style scoped>
.incoming-filter {
  margin-bottom: 1.25rem;
}
.incoming-filter p {
  margin: 0.55rem 0 0;
}
.incoming-departure {
  margin-bottom: 0.8rem;
}
.incoming-pickup {
  margin: 1rem 0 0;
}
</style>
