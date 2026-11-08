<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import { BookingState, type BookingWithTrip } from '../api/generated/trips/v1/trips_pb'
import OperationStatus from '../components/OperationStatus.vue'
import TripCard from '../components/TripCard.vue'
import { useOperation } from '../composables/useOperation'
import { bookingLabel } from '../lib/labels'
import { selectedPersonId } from '../state/session'

const client = clientsForPerson(selectedPersonId.value)
const { pending, error, success, run } = useOperation()
const entries = ref<BookingWithTrip[]>([])
const nextOffset = ref<number>()
const loaded = ref(false)

async function load(append = false) {
  const response = await run((options) =>
    client.bookings.listMyBookings(
      { offset: append ? nextOffset.value : 0, pageSize: 20 },
      options,
    ),
  )
  if (response) {
    entries.value = append ? [...entries.value, ...response.entries] : response.entries
    nextOffset.value = response.nextOffset
    loaded.value = true
  }
}

async function withdraw(id: string) {
  const response = await run(async (options) => {
    await client.bookings.withdrawBooking({ bookingId: id }, options)
    return client.bookings.listMyBookings({ pageSize: 20 }, options)
  })
  if (response) {
    entries.value = response.entries
    nextOffset.value = response.nextOffset
    success.value = 'Deine Anfrage bzw. Teilnahme wurde zurückgezogen.'
  }
}

onMounted(() => load())
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Meine Anfragen</h1>
      <p>
        Deine angefragten und bestätigten Mitfahrten. Vor der Abfahrt kannst du eine Teilnahme
        zurückziehen.
      </p>
    </div>
    <RouterLink class="button secondary" to="/">Fahrten finden</RouterLink>
  </header>
  <OperationStatus :error="error" :success="success" />
  <p v-if="pending && !loaded" role="status">Deine Anfragen werden geladen …</p>
  <div v-if="error && !loaded" class="actions">
    <button class="secondary" @click="load()">Erneut versuchen</button>
  </div>
  <section v-if="loaded && !entries.length" class="card empty">
    <h2>Noch keine Anfragen</h2>
    <p>Suche eine Fahrt und frage in den Fahrtdetails einen Platz an.</p>
  </section>
  <div class="stack">
    <section v-for="entry in entries" :key="entry.booking?.id" class="stack" style="gap: 0.5rem">
      <div v-if="entry.booking" class="split">
        <span
          class="badge"
          :class="{
            success: entry.booking.state === BookingState.ACCEPTED,
            warning: entry.booking.state === BookingState.PENDING,
          }"
        >
          {{ bookingLabel(entry.booking.state) }}
        </span>
        <button
          v-if="entry.booking.canWithdraw"
          class="danger"
          :disabled="pending"
          @click="withdraw(entry.booking.id)"
        >
          Teilnahme zurückziehen
        </button>
      </div>
      <TripCard v-if="entry.trip" :trip="entry.trip" />
    </section>
  </div>
  <div v-if="nextOffset !== undefined" class="actions">
    <button class="secondary" :disabled="pending" @click="load(true)">
      Weitere Anfragen laden
    </button>
  </div>
</template>
