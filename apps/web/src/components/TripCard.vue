<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { TripState, type Trip } from '../api/generated/trips/v1/trips_pb'
import { formatDate, formatDecimal } from '../lib/format'
import { formatCents, tripLabel } from '../lib/labels'

defineProps<{ trip: Trip }>()
</script>

<template>
  <article class="card">
    <div class="split" style="margin-bottom: 1rem">
      <strong>{{ formatDate(trip.departureAt) }}</strong>
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
        <small class="route-label">Start</small>
        {{ trip.origin }}
      </p>
      <span aria-hidden="true">→</span>
      <p>
        <small class="route-label">Ziel</small>
        {{ trip.destination }}
      </p>
    </div>
    <div class="meta-row">
      <span>
        {{ trip.driverDisplayName || 'Fahrer' }}
        <template v-if="trip.isOwner">· Du fährst</template>
      </span>
      <span v-if="trip.state === TripState.PLANNED">
        {{ trip.availableSeats }} von {{ trip.seatsTotal }} Plätzen frei
      </span>
      <span>{{ formatDecimal(trip.distanceKm) }} km geplant</span>
      <span v-if="trip.seriesId">Wöchentliche Fahrt</span>
    </div>
    <div class="split actions">
      <span v-if="trip.costPreview && trip.state === TripState.PLANNED" class="small muted">
        Voraussichtlich {{ formatCents(trip.costPreview.approximateShareCents) }} pro Person
      </span>
      <RouterLink class="button secondary" :to="`/fahrten/${trip.id}`">Fahrtdetails</RouterLink>
    </div>
  </article>
</template>
