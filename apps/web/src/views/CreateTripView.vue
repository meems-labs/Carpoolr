<script setup lang="ts">
import { reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { timestampFromDate } from '@bufbuild/protobuf/wkt'
import { clientsForPerson } from '../api/application'
import OperationStatus from '../components/OperationStatus.vue'
import { useOperation } from '../composables/useOperation'
import { berlinDateTime, decimal, localDateTime, normalizeAddress } from '../lib/format'
import { weekdays } from '../lib/labels'
import { currentProfile, selectedPersonId } from '../state/session'

const router = useRouter()
const client = clientsForPerson(selectedPersonId.value)
const { pending, error, run } = useOperation()
const recurring = ref(false)
const form = reactive({
  origin: currentProfile.value?.pickupAddress ?? '',
  destination: '',
  departure: '',
  seats: currentProfile.value?.vehicle?.passengerSeats ?? 1,
  distance: '',
  startDate: '',
  endDate: '',
  time: '',
  weekdays: [] as number[],
})
let lastAttempt: { fingerprint: string; id: string } | undefined

function requestId(payload: object): string {
  const fingerprint = JSON.stringify(payload)
  if (lastAttempt?.fingerprint !== fingerprint)
    lastAttempt = { fingerprint, id: crypto.randomUUID() }
  return lastAttempt!.id
}

async function submit() {
  const response = await run(async (options) => {
    const common = {
      origin: normalizeAddress(form.origin),
      destination: normalizeAddress(form.destination),
      seatsTotal: form.seats,
      distanceKm: decimal(form.distance, 10000),
    }
    if (recurring.value) {
      if (!form.weekdays.length) throw new Error('Bitte mindestens einen Wochentag auswählen.')
      if (!form.startDate || !form.endDate || form.endDate < form.startDate)
        throw new Error('Bitte einen gültigen Zeitraum für die Serie eingeben.')
      const payload = {
        ...common,
        startDate: form.startDate,
        endDate: form.endDate,
        localDepartureTime: form.time,
        weekdays: [...form.weekdays].sort(),
      }
      await client.series.createSeries({ ...payload, requestId: requestId(payload) }, options)
      return { path: '/meine-fahrten' }
    }
    const departure = berlinDateTime(form.departure)
    if (departure.getTime() <= Date.now())
      throw new Error('Die Abfahrt muss in der Zukunft liegen.')
    const payload = { ...common, departureAt: timestampFromDate(departure) }
    const id = requestId({ ...common, departure: departure.toISOString() })
    const created = await client.trips.createTrip({ ...payload, requestId: id }, options)
    if (!created.trip) throw new Error('Der Server hat keine Fahrt zurückgegeben.')
    return { path: `/fahrten/${created.trip.id}` }
  })
  if (response) await router.push(response.path)
}
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Fahrt anbieten</h1>
      <p>Eine einzelne Fahrt oder regelmäßige Termine mit deinem privaten Auto.</p>
    </div>
  </header>
  <section v-if="!currentProfile?.isSaved || !currentProfile.vehicle" class="card empty">
    <h2>Zuerst dein Auto hinterlegen</h2>
    <p>Für ein Fahrtangebot brauchst du ein vollständiges Profil mit Abholadresse und Fahrzeug.</p>
    <RouterLink class="button" to="/profil">Profil ergänzen</RouterLink>
  </section>
  <form v-else class="card stack" @submit.prevent="submit">
    <OperationStatus :error="error" />
    <fieldset class="stack" :disabled="pending">
      <div class="grid">
        <label>
          Vollständige Startadresse
          <textarea v-model="form.origin" required maxlength="500" />
        </label>
        <label>
          Vollständige Zieladresse
          <textarea v-model="form.destination" required maxlength="500" />
        </label>
        <label>
          Angebotene Mitfahrerplätze
          <input
            v-model.number="form.seats"
            type="number"
            min="1"
            :max="currentProfile.vehicle.passengerSeats"
            required
          />
          <small>
            Ohne Fahrer; maximal {{ currentProfile.vehicle.passengerSeats }} laut deinem Profil.
          </small>
        </label>
        <label>
          Geschätzte gesamte Strecke in km
          <input v-model="form.distance" inputmode="decimal" required />
          <small>
            Bitte selbst ermitteln und eingeben, einschließlich geplanter Abholwege. Es wird keine
            Route automatisch berechnet.
          </small>
        </label>
      </div>
      <hr class="divider" />
      <label class="checkbox">
        <input v-model="recurring" type="checkbox" />
        Wöchentlich wiederholen
      </label>
      <label v-if="!recurring">
        Abfahrt (Europe/Berlin)
        <input v-model="form.departure" type="datetime-local" :min="localDateTime()" required />
      </label>
      <template v-else>
        <div class="grid">
          <label>
            Erster möglicher Tag
            <input
              v-model="form.startDate"
              type="date"
              :min="localDateTime().slice(0, 10)"
              required
            />
          </label>
          <label>
            Letzter möglicher Tag
            <input v-model="form.endDate" type="date" :min="form.startDate" required />
          </label>
          <label>
            Abfahrtszeit (Europe/Berlin)
            <input v-model="form.time" type="time" required />
          </label>
        </div>
        <fieldset>
          <legend>Wochentage</legend>
          <div class="weekdays">
            <label v-for="day in weekdays" :key="day.value">
              <input v-model="form.weekdays" type="checkbox" :value="day.value" />
              {{ day.label }}
            </label>
          </div>
        </fieldset>
        <p class="hint">
          Jeder Termin ist eine eigene Fahrt. Einzelne Termine und alle zukünftigen Termine einer
          Serie lassen sich separat absagen. Doppeldeutige oder nicht existierende Uhrzeiten bei der
          Zeitumstellung werden abgelehnt.
        </p>
      </template>
      <p class="hint">
        Kosten werden gleichmäßig auf Fahrer und bestätigte Mitfahrer verteilt. Eine Rückfahrt wird
        als eigenes Angebot angelegt.
      </p>
      <div class="actions">
        <button type="submit">
          {{
            pending ? 'Wird gespeichert …' : recurring ? 'Fahrtenserie anbieten' : 'Fahrt anbieten'
          }}
        </button>
        <RouterLink class="button secondary" to="/">Abbrechen</RouterLink>
      </div>
    </fieldset>
  </form>
</template>
