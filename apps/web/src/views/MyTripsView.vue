<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import type { RecurringSeries, Trip } from '../api/generated/trips/v1/trips_pb'
import OperationStatus from '../components/OperationStatus.vue'
import TripCard from '../components/TripCard.vue'
import { useOperation } from '../composables/useOperation'
import { weekdays } from '../lib/labels'
import { selectedPersonId } from '../state/session'

const client = clientsForPerson(selectedPersonId.value)
const { pending, error, success, run } = useOperation()
const trips = ref<Trip[]>([])
const series = ref<RecurringSeries[]>([])
const nextTrips = ref<number>()
const nextSeries = ref<number>()
const loaded = ref(false)

async function load() {
  const response = await run(async (options) =>
    Promise.all([
      client.trips.listMyTrips({ pageSize: 20 }, options),
      client.series.listMySeries({ pageSize: 20 }, options),
    ]),
  )
  if (response) {
    trips.value = response[0].trips
    nextTrips.value = response[0].nextOffset
    series.value = response[1].series
    nextSeries.value = response[1].nextOffset
    loaded.value = true
  }
}

async function moreTrips() {
  const response = await run((options) =>
    client.trips.listMyTrips({ offset: nextTrips.value, pageSize: 20 }, options),
  )
  if (response) {
    trips.value.push(...response.trips)
    nextTrips.value = response.nextOffset
  }
}

async function moreSeries() {
  const response = await run((options) =>
    client.series.listMySeries({ offset: nextSeries.value, pageSize: 20 }, options),
  )
  if (response) {
    series.value.push(...response.series)
    nextSeries.value = response.nextOffset
  }
}

async function cancelSeries(id: string) {
  const response = await run(async (options) => {
    await client.series.cancelSeries({ seriesId: id }, options)
    return Promise.all([
      client.trips.listMyTrips({ pageSize: 20 }, options),
      client.series.listMySeries({ pageSize: 20 }, options),
    ])
  })
  if (response) {
    trips.value = response[0].trips
    nextTrips.value = response[0].nextOffset
    series.value = response[1].series
    nextSeries.value = response[1].nextOffset
    success.value = 'Die zukünftigen Fahrten dieser Serie wurden abgesagt.'
  }
}

function seriesDays(values: number[]) {
  return weekdays
    .filter((day) => values.includes(day.value))
    .map((day) => day.label)
    .join(', ')
}
onMounted(load)
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Meine Angebote</h1>
      <p>Deine einzelnen Fahrten und regelmäßigen Fahrtenserien.</p>
    </div>
    <RouterLink class="button" to="/fahrten/neu">Fahrt anbieten</RouterLink>
  </header>
  <OperationStatus :error="error" :success="success" />
  <p v-if="pending && !loaded" role="status">Deine Angebote werden geladen …</p>
  <div v-if="error && !loaded" class="actions">
    <button class="secondary" @click="load">Erneut versuchen</button>
  </div>
  <div class="stack">
    <section v-if="series.length" class="stack">
      <h2>Wöchentliche Serien</h2>
      <article v-for="item in series" :key="item.id" class="card">
        <div class="split">
          <h3>{{ seriesDays(item.weekdays) }} · {{ item.localDepartureTime }} Uhr</h3>
          <span class="badge" :class="{ danger: item.isCancelled }">
            {{ item.isCancelled ? 'Serie abgesagt' : 'Wöchentlich' }}
          </span>
        </div>
        <p class="address">{{ item.origin }} → {{ item.destination }}</p>
        <p class="small muted">{{ item.startDate }} bis {{ item.endDate }} · Europe/Berlin</p>
        <template v-if="!item.isCancelled">
          <p class="hint">
            Einzelne Termine kannst du in den Fahrtdetails absagen. Die folgende Aktion sagt alle
            zukünftigen, noch geplanten Termine ab.
          </p>
          <button class="danger" :disabled="pending" @click="cancelSeries(item.id)">
            Alle zukünftigen Termine absagen
          </button>
        </template>
      </article>
      <button
        v-if="nextSeries !== undefined"
        class="secondary"
        :disabled="pending"
        @click="moreSeries"
      >
        Weitere Serien laden
      </button>
    </section>
    <section class="stack">
      <h2>Alle eigenen Fahrten</h2>
      <div v-if="loaded && !trips.length" class="card empty">
        <p>Du hast noch keine Fahrten angeboten.</p>
      </div>
      <TripCard v-for="trip in trips" :key="trip.id" :trip="trip" />
      <button
        v-if="nextTrips !== undefined"
        class="secondary"
        :disabled="pending"
        @click="moreTrips"
      >
        Weitere eigene Fahrten laden
      </button>
    </section>
  </div>
</template>
