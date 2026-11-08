<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { timestampDate } from '@bufbuild/protobuf/wkt'
import { TripState, type Trip } from '../api/generated/trips/v1/trips_pb'
import { formatDate, formatDecimal } from '../lib/format'
import { formatCents, tripLabel } from '../lib/labels'
import AppIcon from './AppIcon.vue'

const props = withDefaults(defineProps<{ trip: Trip; featured?: boolean }>(), { featured: false })
const departure = computed(() => {
  if (!props.trip.departureAt) return undefined
  const value = timestampDate(props.trip.departureAt)
  const options = { timeZone: 'Europe/Berlin' }
  return {
    iso: value.toISOString(),
    day: new Intl.DateTimeFormat('de-DE', { ...options, day: '2-digit' }).format(value),
    month: new Intl.DateTimeFormat('de-DE', { ...options, month: 'short' }).format(value),
    weekday: new Intl.DateTimeFormat('de-DE', { ...options, weekday: 'long' }).format(value),
    time: new Intl.DateTimeFormat('de-DE', {
      ...options,
      hour: '2-digit',
      minute: '2-digit',
    }).format(value),
    year: new Intl.DateTimeFormat('de-DE', { ...options, year: 'numeric' }).format(value),
  }
})
</script>

<template>
  <article class="trip-card" :class="{ 'trip-card--featured': featured }">
    <div class="trip-card__header">
      <div v-if="departure" class="trip-date" aria-hidden="true">
        <span>{{ departure.month }}</span>
        <strong>{{ departure.day }}</strong>
        <small>{{ departure.year }}</small>
      </div>
      <div class="trip-card__departure">
        <span class="trip-card__weekday">{{ departure?.weekday ?? 'Abfahrt' }}</span>
        <time v-if="departure" :datetime="departure.iso" :title="formatDate(trip.departureAt)">
          {{ departure.time }}
          <span>Uhr</span>
        </time>
        <p>
          {{ trip.isOwner ? 'Du fährst' : `Mit ${trip.driverDisplayName || 'deinem Kollegen'}` }}
        </p>
      </div>
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
    <ol class="trip-stops" aria-label="Fahrtstrecke">
      <li>
        <small>Start</small>
        <p>{{ trip.origin }}</p>
      </li>
      <li>
        <small>Ziel</small>
        <p>{{ trip.destination }}</p>
      </li>
    </ol>
    <div class="trip-card__meta">
      <span v-if="trip.state === TripState.PLANNED">
        <AppIcon name="users" :size="17" />
        {{ trip.availableSeats }} von {{ trip.seatsTotal }} Plätzen frei
      </span>
      <span>
        <AppIcon name="pin" :size="17" />
        {{ formatDecimal(trip.distanceKm) }} km
      </span>
      <span v-if="trip.seriesId">
        <AppIcon name="repeat" :size="17" />
        Wöchentlich
      </span>
    </div>
    <div class="trip-card__footer">
      <div v-if="trip.costPreview && trip.state === TripState.PLANNED" class="trip-price">
        <strong>{{ formatCents(trip.costPreview.approximateShareCents) }}</strong>
        <span>voraussichtlich pro Person</span>
      </div>
      <span v-else class="small muted">{{ tripLabel(trip.state) }}</span>
      <RouterLink class="button secondary" :to="`/fahrten/${trip.id}`">
        Fahrtdetails
        <AppIcon name="arrow-up-right" :size="18" />
      </RouterLink>
    </div>
  </article>
</template>

<style scoped>
.trip-card {
  min-width: 0;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  padding: 1.25rem;
}
.trip-card--featured {
  border-color: var(--selected-border);
}
.trip-card__header {
  display: flex;
  align-items: center;
  gap: 1rem;
}
.trip-date {
  width: 64px;
  min-height: 84px;
  flex: 0 0 auto;
  display: grid;
  justify-items: center;
  align-content: center;
  padding: 0.6rem;
  background: var(--selected-surface);
  color: var(--ink);
  border-radius: 14px;
  line-height: 1.1;
}
.trip-card--featured .trip-date {
  background: var(--accent);
  color: var(--on-accent);
}
.trip-card--featured .trip-date small {
  color: var(--on-accent);
}
.trip-date > span {
  font-size: 0.65rem;
  font-weight: 800;
  text-transform: uppercase;
}
.trip-date strong {
  font-size: 1.9rem;
  letter-spacing: -0.05em;
}
.trip-date small {
  margin-top: 0.1rem;
  font-size: 0.6rem;
  color: var(--muted);
}
.trip-card__departure {
  flex: 1;
  min-width: 0;
}
.trip-card__weekday {
  display: block;
  font-size: 0.75rem;
  color: var(--muted);
  margin-bottom: 0.15rem;
}
.trip-card__departure time {
  display: block;
  color: var(--ink);
  font-size: 1.7rem;
  font-weight: 800;
  letter-spacing: -0.045em;
  line-height: 1.15;
}
.trip-card__departure time > span {
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0;
  color: var(--muted);
}
.trip-card__departure p {
  margin: 0.2rem 0 0;
  font-size: 0.75rem;
  color: var(--muted);
  overflow-wrap: anywhere;
}
.trip-card__header > .badge {
  align-self: flex-start;
}
.trip-stops {
  list-style: none;
  padding: 0 0 0 1.7rem;
  margin: 1.65rem 0 1.25rem;
}
.trip-stops li {
  position: relative;
  padding-bottom: 1rem;
}
.trip-stops li:last-child {
  padding-bottom: 0;
}
.trip-stops li::before {
  content: '';
  position: absolute;
  left: -1.55rem;
  top: 0.35rem;
  width: 10px;
  height: 10px;
  background: var(--surface);
  border: 2px solid var(--brand);
  border-radius: 50%;
}
.trip-stops li:last-child::before {
  border-radius: 3px;
  background: var(--brand);
}
.trip-stops li:first-child::after {
  content: '';
  position: absolute;
  top: 1rem;
  left: -1.3rem;
  height: calc(100% - 0.55rem);
  border-left: 1px solid var(--control-border);
}
.trip-stops small {
  display: block;
  font-size: 0.65rem;
  font-weight: 650;
  color: var(--muted);
  margin-bottom: 0.15rem;
}
.trip-stops p {
  margin: 0;
  font-size: 0.9rem;
  line-height: 1.45;
  font-weight: 650;
  white-space: pre-line;
  overflow-wrap: anywhere;
}
.trip-card__meta {
  display: flex;
  gap: 0.5rem 1rem;
  flex-wrap: wrap;
  color: var(--muted);
  font-size: 0.7rem;
}
.trip-card__meta > span {
  display: inline-flex;
  gap: 0.35rem;
  align-items: center;
}
.trip-card__footer {
  border-top: 1px solid var(--line);
  padding-top: 1.1rem;
  margin-top: 1.1rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  flex-wrap: wrap;
}
.trip-price strong {
  display: block;
  color: var(--ink);
  font-size: 1.3rem;
  font-weight: 800;
  letter-spacing: -0.035em;
  font-variant-numeric: tabular-nums;
  line-height: 1.3;
}
.trip-price > span {
  font-size: 0.65rem;
  color: var(--muted);
}
.trip-card__footer .button {
  min-height: 44px;
  padding: 0.65rem 0.85rem;
  font-size: 0.75rem;
}
@media (min-width: 640px) {
  .trip-card {
    padding: 1.6rem;
  }
  .trip-card__departure time {
    font-size: 1.95rem;
  }
}
@media (max-width: 359px) {
  .trip-card__header {
    gap: 0.65rem;
    flex-wrap: wrap;
  }
  .trip-card__header > .badge {
    margin-left: auto;
  }
  .trip-card__footer .button {
    width: 100%;
  }
}
</style>
