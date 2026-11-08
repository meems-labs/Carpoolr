<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import { EnergyKind, type UserProfile } from '../api/generated/trips/v1/trips_pb'
import OperationStatus from '../components/OperationStatus.vue'
import PwaControls from '../components/PwaControls.vue'
import { useOperation } from '../composables/useOperation'
import { useUnsavedChanges } from '../composables/useUnsavedChanges'
import { decimal, normalizeAddress } from '../lib/format'
import { unitLabel } from '../lib/labels'
import { currentProfile, selectedPersonId } from '../state/session'

const personId = selectedPersonId.value
const client = clientsForPerson(personId)
const { pending, error, success, run } = useOperation()
const profile = currentProfile.value
const hasVehicle = ref(!!profile?.vehicle)
const form = reactive({
  displayName: profile?.displayName ?? '',
  pickupAddress: profile?.pickupAddress ?? '',
  vehicleName: profile?.vehicle?.name ?? '',
  seats: profile?.vehicle?.passengerSeats ?? 1,
  energy: profile?.vehicle?.energyKind ?? EnergyKind.UNSPECIFIED,
  consumption: profile?.vehicle?.consumptionPer100Km ?? '',
  price: profile?.vehicle?.unitPriceEur ?? '',
})
const { isDirty, markSaved } = useUnsavedChanges(() => ({ ...form, hasVehicle: hasVehicle.value }))

function populateProfile(saved: UserProfile) {
  form.displayName = saved.displayName
  form.pickupAddress = saved.pickupAddress
  hasVehicle.value = !!saved.vehicle
  form.vehicleName = saved.vehicle?.name ?? ''
  form.seats = saved.vehicle?.passengerSeats ?? 1
  form.energy = saved.vehicle?.energyKind ?? EnergyKind.UNSPECIFIED
  form.consumption = saved.vehicle?.consumptionPer100Km ?? ''
  form.price = saved.vehicle?.unitPriceEur ?? ''
}

watch(
  currentProfile,
  (loaded) => {
    if (!loaded || personId !== selectedPersonId.value || isDirty.value) return
    populateProfile(loaded)
    markSaved()
  },
  { flush: 'sync' },
)

async function save() {
  const response = await run((options) => {
    if (hasVehicle.value && form.energy === EnergyKind.UNSPECIFIED) {
      throw new Error('Bitte den Antrieb deines Autos auswählen.')
    }
    return client.profiles.updateMyProfile(
      {
        displayName: form.displayName.trim(),
        pickupAddress: normalizeAddress(form.pickupAddress),
        vehicle: hasVehicle.value
          ? {
              name: form.vehicleName.trim(),
              passengerSeats: form.seats,
              energyKind: form.energy,
              consumptionPer100Km: decimal(form.consumption, 1000),
              unitPriceEur: decimal(form.price, 100),
            }
          : undefined,
      },
      options,
    )
  })
  if (response?.profile && personId === selectedPersonId.value) {
    populateProfile(response.profile)
    currentProfile.value = response.profile
    markSaved()
    success.value = 'Dein Profil wurde gespeichert.'
  }
}
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Mein Profil</h1>
      <p>Deine Abholadresse und optional dein Auto für eigene Fahrtangebote.</p>
    </div>
  </header>
  <OperationStatus :error="error" :success="success" />
  <form class="card stack" @submit.prevent="save">
    <fieldset :disabled="pending" class="stack">
      <div class="grid">
        <label class="wide">
          Name
          <input v-model="form.displayName" autocomplete="name" required maxlength="100" />
        </label>
        <label class="wide">
          Vollständige Abholadresse
          <textarea
            v-model="form.pickupAddress"
            autocomplete="street-address"
            required
            maxlength="500"
          />
          <small>
            Bitte Straße, Hausnummer, Postleitzahl und Ort angeben. Der Fahrer sieht diese Adresse
            nach seiner Zusage.
          </small>
        </label>
      </div>
      <hr class="divider" />
      <label class="checkbox">
        <input v-model="hasVehicle" type="checkbox" />
        Ich möchte mit meinem Auto Fahrten anbieten
      </label>
      <div v-if="hasVehicle" class="grid">
        <label>
          Bezeichnung des Autos
          <input v-model="form.vehicleName" required maxlength="100" />
        </label>
        <label>
          Mitfahrerplätze ohne Fahrer
          <input v-model.number="form.seats" type="number" min="1" max="8" required />
        </label>
        <label>
          Antrieb
          <select v-model="form.energy" required>
            <option :value="EnergyKind.UNSPECIFIED" disabled>Bitte auswählen …</option>
            <option :value="EnergyKind.PETROL">Benzin</option>
            <option :value="EnergyKind.DIESEL">Diesel</option>
            <option :value="EnergyKind.ELECTRIC">Elektro</option>
          </select>
        </label>
        <label>
          Verbrauch pro 100 km ({{ unitLabel(form.energy) }})
          <input v-model="form.consumption" inputmode="decimal" required />
        </label>
        <label>
          Preis pro {{ unitLabel(form.energy) }} in EUR
          <input v-model="form.price" inputmode="decimal" required />
        </label>
        <p class="hint">
          Diese Werte bilden die Grundlage für die Kostenvorschau. Beim Abschluss einer Fahrt prüfst
          du die tatsächlichen Werte.
        </p>
      </div>
      <div class="actions">
        <button type="submit">{{ pending ? 'Wird gespeichert …' : 'Profil speichern' }}</button>
        <RouterLink class="button secondary" to="/fahrten">Fahrten finden</RouterLink>
      </div>
    </fieldset>
  </form>
  <section class="card" style="margin-top: 1.5rem">
    <p class="eyebrow">Auf deinem Gerät</p>
    <h2>Carpoolr als App</h2>
    <p class="small muted">
      Installiere die App für deinen nächsten Arbeitsweg. Neue Versionen werden automatisch geladen,
      sobald deine Eingaben gespeichert sind.
    </p>
    <PwaControls mode="settings" />
  </section>
</template>
