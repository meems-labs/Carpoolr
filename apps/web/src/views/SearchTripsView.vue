<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { timestampFromDate } from '@bufbuild/protobuf/wkt'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import type { SearchTripsRequest, Trip } from '../api/generated/trips/v1/trips_pb'
import OperationStatus from '../components/OperationStatus.vue'
import TripCard from '../components/TripCard.vue'
import { useOperation } from '../composables/useOperation'
import { berlinDay, localDateTime, normalizeAddress } from '../lib/format'
import { currentProfile, selectedPersonId } from '../state/session'

const client = clientsForPerson(selectedPersonId.value)
const { pending, error, run } = useOperation()
const filters = reactive({ date: localDateTime().slice(0, 10), origin: '', destination: '' })
const trips = ref<Trip[]>([])
const nextOffset = ref<number>()
const searched = ref(false)
let activeFilters: Omit<SearchTripsRequest, '$typeName' | 'offset'> | undefined

async function search(append = false) {
  const response = await run(async (options) => {
    if (!append || !activeFilters) {
      const window = berlinDay(filters.date)
      activeFilters = {
        origin: normalizeAddress(filters.origin),
        destination: normalizeAddress(filters.destination),
        departureFrom: timestampFromDate(window.from),
        departureUntil: timestampFromDate(window.until),
        pageSize: 20,
      }
    }
    return client.trips.searchTrips(
      { ...activeFilters, offset: append ? (nextOffset.value ?? 0) : 0 },
      options,
    )
  })
  if (response) {
    trips.value = append ? [...trips.value, ...response.trips] : response.trips
    nextOffset.value = response.nextOffset
    searched.value = true
  }
}

onMounted(() => search())
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Fahrten finden</h1>
      <p>Finde ein Angebot für deinen Weg zur Arbeit oder zurück.</p>
    </div>
    <RouterLink class="button" to="/fahrten/neu">Fahrt anbieten</RouterLink>
  </header>
  <p v-if="!currentProfile?.isSaved" class="notice">
    Bevor du einen Platz anfragst, ergänze bitte
    <RouterLink to="/profil">dein Profil und deine Abholadresse</RouterLink>
    .
  </p>
  <form class="card" style="margin-bottom: 1.5rem" @submit.prevent="search()">
    <fieldset :disabled="pending" class="grid">
      <label>
        Datum
        <input v-model="filters.date" type="date" required />
        <small>Europe/Berlin</small>
      </label>
      <label>
        Startadresse (optional)
        <input v-model="filters.origin" maxlength="500" />
        <small>Genauer Start wie im Angebot</small>
      </label>
      <label>
        Zieladresse (optional)
        <input v-model="filters.destination" maxlength="500" />
        <small>Genaues Ziel wie im Angebot</small>
      </label>
      <div class="actions" style="align-self: center; margin-top: 0">
        <button type="submit">{{ pending ? 'Suche läuft …' : 'Fahrten suchen' }}</button>
      </div>
    </fieldset>
  </form>
  <OperationStatus :error="error" />
  <p v-if="pending && !searched" role="status">Fahrten werden geladen …</p>
  <section v-if="searched && !trips.length && !pending" class="card empty">
    <h2>Keine passenden Fahrten</h2>
    <p>Versuche ein anderes Datum oder biete selbst eine Fahrt an.</p>
  </section>
  <div class="trip-list"><TripCard v-for="trip in trips" :key="trip.id" :trip="trip" /></div>
  <div v-if="nextOffset !== undefined" class="actions">
    <button class="secondary" :disabled="pending" @click="search(true)">
      {{ pending ? 'Wird geladen …' : 'Weitere Fahrten laden' }}
    </button>
  </div>
</template>
