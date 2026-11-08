<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { timestampDate } from '@bufbuild/protobuf/wkt'
import { clientsForPerson } from '../api/application'
import {
  BookingState,
  TripState,
  type Bill,
  type GetTripResponse,
} from '../api/generated/trips/v1/trips_pb'
import { type CallOptions } from '../api/grpc'
import OperationStatus from '../components/OperationStatus.vue'
import { useOperation } from '../composables/useOperation'
import { useUnsavedChanges } from '../composables/useUnsavedChanges'
import { decimal, formatDate, formatDecimal } from '../lib/format'
import {
  bookingLabel,
  energyLabel,
  formatCents,
  paymentLabel,
  tripLabel,
  unitLabel,
} from '../lib/labels'
import { currentProfile, selectedPersonId } from '../state/session'
import { requestDashboardRefresh } from '../state/dashboard'

const route = useRoute()
const tripId = String(route.params.id)
const client = clientsForPerson(selectedPersonId.value)
const { pending, error, success, run } = useOperation()
const details = ref<GetTripResponse>()
const bill = ref<Bill>()
const trip = computed(() => details.value?.trip)
const completion = reactive({ distance: '', consumption: '', price: '' })
const { isDirty: completionDirty, markSaved: markCompletionSaved } = useUnsavedChanges(() => ({
  ...completion,
}))
const requestIds = new Map<string, string>()

const canRequest = computed(() => {
  const value = trip.value
  return (
    !!value &&
    !value.isOwner &&
    value.state === TripState.PLANNED &&
    value.availableSeats > 0 &&
    !!value.departureAt &&
    timestampDate(value.departureAt).getTime() > Date.now() &&
    (!value.myBooking ||
      [BookingState.REJECTED, BookingState.WITHDRAWN].includes(value.myBooking.state))
  )
})

const mapsUrl = computed(() => {
  try {
    const url = new URL(details.value?.routeUrl ?? '')
    return url.protocol === 'https:' ? url.href : ''
  } catch {
    return ''
  }
})

function requestId(key: string): string {
  if (!requestIds.has(key)) requestIds.set(key, crypto.randomUUID())
  return requestIds.get(key)!
}

async function refresh(options: CallOptions) {
  const response = await client.trips.getTrip({ tripId }, options)
  if (!response.trip) throw new Error('Die Fahrt konnte nicht geladen werden.')
  let loadedBill: Bill | undefined
  if (
    response.trip.state === TripState.COMPLETED &&
    (response.trip.isOwner || response.trip.myBooking?.state === BookingState.ACCEPTED)
  ) {
    loadedBill = (await client.billing.getBill({ tripId }, options)).bill
  }
  details.value = response
  bill.value = loadedBill
  // Refreshing bookings must not overwrite completion values the driver is still editing.
  // A successful completion or cancellation closes the form and establishes a fresh baseline.
  if (!completionDirty.value || response.trip.state !== TripState.PLANNED) {
    completion.distance = loadedBill?.distanceKm ?? response.trip.distanceKm
    completion.consumption =
      loadedBill?.consumptionPer100Km ?? response.trip.vehicle?.consumptionPer100Km ?? ''
    completion.price = loadedBill?.unitPriceEur ?? response.trip.vehicle?.unitPriceEur ?? ''
    markCompletionSaved()
  }
}

async function load() {
  await run(refresh)
}

async function change(operation: (options: CallOptions) => Promise<unknown>, message: string) {
  const result = await run(async (options) => {
    await operation(options)
    requestDashboardRefresh()
    await refresh(options)
    return true
  })
  if (result) success.value = message
}

function requestSeat() {
  const key = `booking:${trip.value?.myBooking?.id ?? 'new'}:${trip.value?.myBooking?.state ?? 0}`
  return change(
    (options) => client.bookings.requestSeat({ tripId, requestId: requestId(key) }, options),
    'Deine Anfrage wurde an den Fahrer gesendet.',
  )
}

function complete() {
  return change(
    (options) =>
      client.billing.completeTrip(
        {
          tripId,
          distanceKm: decimal(completion.distance, 10000),
          consumptionPer100Km: decimal(completion.consumption, 1000),
          unitPriceEur: decimal(completion.price, 100),
        },
        options,
      ),
    'Die Fahrt ist abgeschlossen. Die Kostenanteile sind jetzt festgeschrieben.',
  )
}

function reportPayment() {
  const mine = bill.value?.shares.find((share) => share.isMine)
  const key = `payment:${mine?.paymentState}`
  return change(async (options) => {
    const response = await client.billing.reportPayment(
      { tripId, requestId: requestId(key) },
      options,
    )
    requestIds.delete(key)
    return response
  }, 'Du hast deine Zahlung gemeldet. Der Fahrer muss den Eingang noch bestätigen.')
}

onMounted(load)
</script>

<template>
  <div class="actions" style="margin: 0 0 1rem">
    <RouterLink to="/fahrten">← Fahrten finden</RouterLink>
  </div>
  <header class="page-header">
    <div>
      <h1>Fahrtdetails</h1>
      <p v-if="trip">{{ formatDate(trip.departureAt) }}</p>
    </div>
    <button class="secondary" :disabled="pending" @click="load">Aktualisieren</button>
  </header>
  <OperationStatus :error="error" :success="success" />
  <p v-if="pending && !trip" role="status">Fahrt wird geladen …</p>
  <div v-if="trip" class="stack">
    <section class="card stack">
      <div class="split">
        <h2>{{ trip.isOwner ? 'Dein Fahrtangebot' : `Mit ${trip.driverDisplayName}` }}</h2>
        <span
          class="badge"
          :class="{
            danger: trip.state === TripState.CANCELLED,
            success: trip.state === TripState.COMPLETED,
          }"
        >
          {{ tripLabel(trip.state) }}
        </span>
      </div>
      <div class="trip-route">
        <p>
          <small class="route-label">Startadresse</small>
          {{ trip.origin }}
        </p>
        <span aria-hidden="true">→</span>
        <p>
          <small class="route-label">Zieladresse</small>
          {{ trip.destination }}
        </p>
      </div>
      <div class="meta-row">
        <span v-if="trip.state === TripState.PLANNED">
          {{ trip.availableSeats }} von {{ trip.seatsTotal }} Plätzen frei
        </span>
        <span>
          {{ formatDecimal(bill?.distanceKm ?? trip.distanceKm) }} km,
          {{ bill ? 'abgerechnet' : 'manuell angegeben' }}
        </span>
        <span v-if="trip.vehicle">
          {{ trip.vehicle.name }} · {{ energyLabel(trip.vehicle.energyKind) }}
        </span>
        <span v-if="trip.seriesId">Termin einer wöchentlichen Serie</span>
      </div>
      <p v-if="trip.seriesId" class="hint">
        Eine Absage betrifft diesen Termin. Die gesamte zukünftige Serie verwaltest du unter „Meine
        Angebote“.
      </p>
      <button
        v-if="trip.canCancel"
        class="danger"
        :disabled="pending"
        @click="
          change(
            (options) => client.trips.cancelTrip({ tripId }, options),
            'Diese Fahrt wurde abgesagt.',
          )
        "
      >
        {{ trip.seriesId ? 'Diesen Termin absagen' : 'Fahrt absagen' }}
      </button>
    </section>

    <section v-if="!trip.isOwner" class="card">
      <h2>Deine Mitfahrt</h2>
      <template v-if="trip.myBooking">
        <p>
          <span
            class="badge"
            :class="{
              success: trip.myBooking.state === BookingState.ACCEPTED,
              warning: trip.myBooking.state === BookingState.PENDING,
            }"
          >
            {{ bookingLabel(trip.myBooking.state) }}
          </span>
        </p>
        <p v-if="trip.myBooking.state === BookingState.PENDING" class="muted">
          Der Fahrer entscheidet über deine Anfrage. Erst eine Zusage reserviert deinen Platz.
        </p>
        <p v-if="trip.myBooking.pickupAddress" class="address small">
          Abholung: {{ trip.myBooking.pickupAddress }}
        </p>
      </template>
      <p v-if="trip.state === TripState.CANCELLED">Diese Fahrt wurde abgesagt.</p>
      <p v-else-if="trip.state === TripState.COMPLETED">Diese Fahrt ist abgeschlossen.</p>
      <template v-if="canRequest">
        <p v-if="!currentProfile?.isSaved">
          Bitte zuerst
          <RouterLink to="/profil">dein Profil mit vollständiger Abholadresse speichern</RouterLink>
          .
        </p>
        <template v-else>
          <p class="hint">
            Deine Abholadresse aus dem Profil wird dem Fahrer nach seiner Zusage angezeigt.
          </p>
          <button :disabled="pending" @click="requestSeat">
            {{ trip.myBooking ? 'Erneut einen Platz anfragen' : 'Einen Platz anfragen' }}
          </button>
        </template>
      </template>
      <p
        v-else-if="trip.state === TripState.PLANNED && trip.availableSeats === 0 && !trip.myBooking"
        class="muted"
      >
        Aktuell sind alle Plätze vergeben.
      </p>
      <div v-if="trip.myBooking?.canWithdraw" class="actions">
        <button
          class="danger"
          :disabled="pending"
          @click="
            change(
              (options) =>
                client.bookings.withdrawBooking({ bookingId: trip!.myBooking!.id }, options),
              'Deine Teilnahme wurde zurückgezogen.',
            )
          "
        >
          Teilnahme zurückziehen
        </button>
      </div>
    </section>

    <section v-if="trip.costPreview && trip.state === TripState.PLANNED" class="card">
      <h2>Kostenvorschau</h2>
      <dl class="definition">
        <dt>Geschätzte Energiekosten</dt>
        <dd>{{ formatCents(trip.costPreview.totalCents) }}</dd>
        <dt>Personen einschließlich Fahrer</dt>
        <dd>{{ trip.costPreview.personCount }}</dd>
        <dt>Voraussichtlich pro Person</dt>
        <dd>{{ formatCents(trip.costPreview.approximateShareCents) }}</dd>
      </dl>
      <p class="hint" style="margin-top: 1rem">
        Strecke × Verbrauch / 100 × Energiepreis. Geteilt durch Fahrer und aktuell bestätigte
        Mitfahrer. Der endgültige Anteil wird nach der Fahrt festgeschrieben und kann wegen Rundung
        um einen Cent abweichen.
      </p>
    </section>

    <section v-if="trip.isOwner" class="card">
      <h2>Mitfahranfragen</h2>
      <p v-if="!details?.bookings.length" class="muted">
        Bisher gibt es keine Anfragen zu dieser Fahrt.
      </p>
      <article v-for="booking in details?.bookings" :key="booking.id" class="participant">
        <div class="split">
          <strong>{{ booking.requesterDisplayName }}</strong>
          <span
            class="badge"
            :class="{
              success: booking.state === BookingState.ACCEPTED,
              warning: booking.state === BookingState.PENDING,
            }"
          >
            {{ bookingLabel(booking.state) }}
          </span>
        </div>
        <p v-if="booking.pickupAddress" class="address small">{{ booking.pickupAddress }}</p>
        <p v-else-if="booking.state === BookingState.PENDING" class="hint">
          Die Abholadresse wird nach deiner Zusage sichtbar.
        </p>
        <div class="actions" v-if="booking.canAccept || booking.canReject">
          <button
            v-if="booking.canAccept"
            :disabled="pending"
            @click="
              change(
                (options) => client.bookings.acceptBooking({ bookingId: booking.id }, options),
                'Die Mitfahranfrage wurde bestätigt.',
              )
            "
          >
            Zusagen
          </button>
          <button
            v-if="booking.canReject"
            class="danger"
            :disabled="pending"
            @click="
              change(
                (options) => client.bookings.rejectBooking({ bookingId: booking.id }, options),
                'Die Mitfahranfrage wurde abgelehnt.',
              )
            "
          >
            Ablehnen
          </button>
        </div>
      </article>
    </section>

    <section v-if="trip.isOwner && mapsUrl" class="card">
      <h2>Route und Abholstopps</h2>
      <ol v-if="details?.routeStops.length">
        <li v-for="(stop, index) in details.routeStops" :key="index">
          <strong>{{ stop.displayName }}</strong>
          <p class="address small">{{ stop.address }}</p>
        </li>
      </ol>
      <p class="hint">
        Die Karte verwendet Start, Ziel und bestätigte Abholadressen. Die Reihenfolge wird nicht
        automatisch optimiert. Die tatsächliche Gesamtstrecke trägst du selbst ein.
      </p>
      <a class="button secondary" :href="mapsUrl" target="_blank" rel="noopener noreferrer">
        Route in Maps öffnen ↗
      </a>
    </section>

    <section v-if="trip.isOwner && trip.state === TripState.PLANNED" class="card">
      <h2>Fahrt abschließen</h2>
      <p v-if="!trip.canComplete" class="muted">
        Nach der geplanten Abfahrt kannst du die Fahrt abschließen und die Kosten abrechnen.
      </p>
      <form v-else @submit.prevent="complete">
        <p class="hint">
          Prüfe die tatsächlich gefahrene Strecke einschließlich Abholwegen und die Energieangaben.
          Der Abschluss schreibt die Kosten und Teilnehmeranteile verbindlich fest.
        </p>
        <fieldset :disabled="pending" class="grid">
          <label>
            Tatsächliche gesamte Strecke (km)
            <input v-model="completion.distance" inputmode="decimal" required />
          </label>
          <label>
            Verbrauch pro 100 km ({{ unitLabel(trip.vehicle?.energyKind ?? 0) }})
            <input v-model="completion.consumption" inputmode="decimal" required />
          </label>
          <label>
            Preis pro {{ unitLabel(trip.vehicle?.energyKind ?? 0) }} (EUR)
            <input v-model="completion.price" inputmode="decimal" required />
          </label>
          <div class="wide actions">
            <button type="submit">Fahrt abschließen und Kosten festschreiben</button>
          </div>
        </fieldset>
      </form>
    </section>

    <section v-if="bill" class="card">
      <h2>Abrechnung</h2>
      <p class="small muted">
        Festgeschrieben am {{ formatDate(bill.completedAt) }}. Zahlungen erfolgen außerhalb der App.
      </p>
      <dl class="definition">
        <dt>Gesamte Strecke</dt>
        <dd>{{ formatDecimal(bill.distanceKm) }} km</dd>
        <dt>Verbrauch pro 100 km</dt>
        <dd>
          {{ formatDecimal(bill.consumptionPer100Km) }}
          {{ unitLabel(trip.vehicle?.energyKind ?? 0) }}
        </dd>
        <dt>Preis pro Einheit</dt>
        <dd>{{ formatDecimal(bill.unitPriceEur) }} EUR</dd>
        <dt>Gesamtkosten</dt>
        <dd>{{ formatCents(bill.totalCents) }}</dd>
      </dl>
      <div class="table-scroll" style="margin-top: 1rem">
        <table data-mobile-cards role="table">
          <thead>
            <tr>
              <th scope="col">Person</th>
              <th scope="col">Anteil</th>
              <th scope="col">Zahlungsstand</th>
              <th scope="col">Aktion</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="share in bill.shares" :key="share.profileId">
              <td data-label="Person">
                {{ share.displayName }}
                <span v-if="share.isMine" class="small muted">(Du)</span>
              </td>
              <td data-label="Anteil">{{ formatCents(share.amountCents) }}</td>
              <td data-label="Zahlungsstand">{{ paymentLabel(share.paymentState) }}</td>
              <td data-label="Aktion">
                <div class="actions" style="margin-top: 0">
                  <button v-if="share.canReport" :disabled="pending" @click="reportPayment">
                    Als bezahlt melden
                  </button>
                  <button
                    v-if="share.canConfirm"
                    :disabled="pending"
                    @click="
                      change(
                        (options) =>
                          client.billing.confirmPayment(
                            { tripId, profileId: share.profileId },
                            options,
                          ),
                        'Der Zahlungseingang wurde bestätigt.',
                      )
                    "
                  >
                    Zahlung bestätigen
                  </button>
                  <button
                    v-if="share.canReject"
                    class="danger"
                    :disabled="pending"
                    @click="
                      change(
                        (options) =>
                          client.billing.rejectPayment(
                            { tripId, profileId: share.profileId },
                            options,
                          ),
                        'Die Zahlung wurde nicht bestätigt.',
                      )
                    "
                  >
                    Zahlung ablehnen
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="hint" style="margin-top: 1rem">
        „Als bezahlt melden“ bestätigt keine Überweisung. Erst der Fahrer bestätigt den
        Zahlungseingang. Bei Ablehnung kann die Zahlung erneut gemeldet werden.
      </p>
    </section>
  </div>
</template>
