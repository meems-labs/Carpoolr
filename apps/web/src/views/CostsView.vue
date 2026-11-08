<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { clientsForPerson } from '../api/application'
import {
  PaymentState,
  type BillResponse,
  type BillShare,
  type CostOverviewTotals,
  type MyBillEntry,
} from '../api/generated/trips/v1/trips_pb'
import type { CallOptions } from '../api/grpc'
import OperationStatus from '../components/OperationStatus.vue'
import { errorMessage, useOperation } from '../composables/useOperation'
import { formatDate, formatDecimal } from '../lib/format'
import { formatCents, paymentLabel } from '../lib/labels'
import { requestDashboardRefresh } from '../state/dashboard'
import { selectedPersonId } from '../state/session'

const client = clientsForPerson(selectedPersonId.value)
const { pending, error, success, run } = useOperation()
const totals = ref<CostOverviewTotals>()
const entries = ref<MyBillEntry[]>([])
const nextOffset = ref<number>()
const loaded = ref(false)
const totalsStale = ref(false)
const reportRequestIds = new Map<string, string>()
const ownOpen = computed(() =>
  totals.value ? totals.value.ownUnpaidCents + totals.value.ownRejectedCents : undefined,
)

async function load(append = false) {
  const response = await run((options) =>
    client.billing.getMyCostOverview(
      {
        offset: append ? (nextOffset.value ?? 0) : 0,
        pageSize: 20,
      },
      options,
    ),
  )
  if (response) {
    totals.value = response.totals
    totalsStale.value = false
    if (append) {
      const merged = new Map(entries.value.map((entry) => [entry.bill?.tripId, entry]))
      for (const entry of response.bills) merged.set(entry.bill?.tripId, entry)
      entries.value = [...merged.values()]
    } else entries.value = response.bills
    nextOffset.value = response.nextOffset
    loaded.value = true
  }
}

async function changePayment(
  operation: (options: CallOptions) => Promise<BillResponse>,
  message: string | ((response: BillResponse) => string),
) {
  let processedMessage = ''
  const result = await run(async (options) => {
    const response = await operation(options)
    requestDashboardRefresh()
    if (!response.bill)
      throw new Error('Der Server hat keine aktualisierte Abrechnung zurückgegeben.')
    const bill = response.bill
    entries.value = entries.value.map((entry) =>
      entry.bill?.tripId === bill.tripId ? { ...entry, bill } : entry,
    )
    processedMessage = typeof message === 'function' ? message(response) : message
    // Refresh global totals without dropping a bill loaded on a later result page.
    try {
      const overview = await client.billing.getMyCostOverview({ pageSize: 1 }, options)
      totals.value = overview.totals
      totalsStale.value = false
    } catch (cause) {
      if (options.signal?.aborted) throw cause
      totalsStale.value = true
      error.value = `Die Zahlungsaktion wurde gespeichert. Die Gesamtsummen konnten nicht aktualisiert werden: ${errorMessage(cause)}`
    }
    return true
  })
  if (result) success.value = processedMessage
}

function reportPayment(tripId: string, share: BillShare) {
  const key = `${tripId}:${share.profileId}`
  if (!reportRequestIds.has(key)) reportRequestIds.set(key, crypto.randomUUID())
  const requestId = reportRequestIds.get(key)!
  return changePayment(
    async (options) => {
      const response = await client.billing.reportPayment({ tripId, requestId }, options)
      // A new user action after a successful RPC is a new report, including repeated rejection cycles.
      reportRequestIds.delete(key)
      return response
    },
    (response) => {
      const state = response.bill?.shares.find((item) => item.isMine)?.paymentState
      if (state === PaymentState.CONFIRMED)
        return 'Diese Zahlung wurde bereits vom Fahrer bestätigt.'
      if (state === PaymentState.REJECTED)
        return 'Die vorherige Meldung wurde abgelehnt. Du kannst die Zahlung erneut melden.'
      return 'Deine Zahlung wurde gemeldet. Der Fahrer muss den Eingang noch bestätigen.'
    },
  )
}

function confirmPayment(tripId: string, profileId: string) {
  return changePayment(
    (options) => client.billing.confirmPayment({ tripId, profileId }, options),
    'Der Zahlungseingang wurde bestätigt.',
  )
}

function rejectPayment(tripId: string, profileId: string) {
  return changePayment(
    (options) => client.billing.rejectPayment({ tripId, profileId }, options),
    'Die Zahlung wurde nicht bestätigt. Die Person kann erneut eine Zahlung melden.',
  )
}

onMounted(() => load())
</script>

<template>
  <header class="page-header">
    <div>
      <h1>Kosten und Zahlungen</h1>
      <p>
        Deine festgeschriebenen Kostenanteile und ausstehenden Erstattungen aus abgeschlossenen
        Fahrten.
      </p>
    </div>
    <button class="secondary" :disabled="pending" @click="load()">Aktualisieren</button>
  </header>
  <OperationStatus :error="error" :success="success" />
  <p v-if="totalsStale" class="notice" role="status">
    Die angezeigten Gesamtsummen können veraltet sein. Bitte die Übersicht aktualisieren.
  </p>
  <p v-if="pending && !loaded" role="status">Deine Kosten und Zahlungen werden geladen …</p>
  <div v-if="error && !loaded" class="actions">
    <button class="secondary" :disabled="pending" @click="load()">Erneut versuchen</button>
  </div>
  <div v-if="totals" class="cost-summary">
    <section class="card">
      <h2>Deine eigenen Kosten</h2>
      <p class="cost-total">{{ formatCents(totals.ownCostCents) }}</p>
      <p class="hint">
        Alle eigenen Anteile, einschließlich deines Fahreranteils. Keine Kostenvorschauen geplanter
        Fahrten.
      </p>
      <dl class="definition">
        <dt>Offene Zahlungsanteile</dt>
        <dd>{{ formatCents(ownOpen) }}</dd>
        <dt>Als bezahlt gemeldet</dt>
        <dd>{{ formatCents(totals.ownReportedCents) }}</dd>
        <dt>Zahlung bestätigt</dt>
        <dd>{{ formatCents(totals.ownConfirmedCents) }}</dd>
      </dl>
      <p class="hint cost-note">
        Offene Zahlungsanteile sind unbezahlte oder abgelehnte Mitfahrerzahlungen. Gemeldete
        Zahlungen warten noch auf die Bestätigung des Fahrers.
      </p>
    </section>
    <section class="card">
      <h2>Erstattungen an dich als Fahrer</h2>
      <p class="cost-total">{{ formatCents(totals.driverTotalReceivableCents) }}</p>
      <p class="hint">
        Mitfahreranteile aus deinen abgeschlossenen Fahrtangeboten, ohne deinen eigenen
        Fahreranteil.
      </p>
      <dl class="definition">
        <dt>Noch ausstehend</dt>
        <dd>{{ formatCents(totals.driverOutstandingCents) }}</dd>
        <dt>Davon als bezahlt gemeldet</dt>
        <dd>{{ formatCents(totals.driverReportedCents) }}</dd>
        <dt>Eingang bestätigt</dt>
        <dd>{{ formatCents(totals.driverConfirmedCents) }}</dd>
      </dl>
      <p class="hint cost-note">
        Gemeldete Zahlungen sind bereits in der ausstehenden Summe enthalten. Erst deine Bestätigung
        zählt als bestätigter Zahlungseingang.
      </p>
    </section>
  </div>
  <section v-if="loaded" class="stack">
    <div class="cost-section-title">
      <h2>Abrechnung je Fahrt</h2>
      <p class="hint">
        Die Gesamtsummen gelten für alle abgeschlossenen Fahrten, unabhängig von den aktuell
        geladenen Einträgen.
      </p>
    </div>
    <section v-if="!entries.length && !pending" class="card empty">
      <h3>Noch keine Abrechnungen</h3>
      <p>
        Sobald eine Fahrt abgeschlossen und ihre Kosten festgeschrieben wurden, erscheinen deine
        Anteile hier.
      </p>
    </section>
    <article v-for="entry in entries" :key="entry.bill?.tripId" class="card">
      <template v-if="entry.bill">
        <div class="split">
          <h3>{{ formatDate(entry.departureAt) }}</h3>
          <span class="badge">
            {{ entry.bill.isDriver ? 'Du bist gefahren' : 'Du bist mitgefahren' }}
          </span>
        </div>
        <div class="trip-route">
          <p>
            <small class="route-label">Start</small>
            {{ entry.origin }}
          </p>
          <span aria-hidden="true">→</span>
          <p>
            <small class="route-label">Ziel</small>
            {{ entry.destination }}
          </p>
        </div>
        <div class="meta-row">
          <span>{{ formatDecimal(entry.bill.distanceKm) }} km abgerechnet</span>
          <span>Gesamte Fahrt: {{ formatCents(entry.bill.totalCents) }}</span>
          <span>Festgeschrieben: {{ formatDate(entry.bill.completedAt) }}</span>
        </div>
        <div class="table-scroll cost-shares">
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
              <tr v-for="share in entry.bill.shares" :key="share.profileId">
                <td data-label="Person">
                  {{ share.displayName }}
                  <span v-if="share.isMine" class="small muted">(Du)</span>
                </td>
                <td data-label="Anteil">{{ formatCents(share.amountCents) }}</td>
                <td data-label="Zahlungsstand">{{ paymentLabel(share.paymentState) }}</td>
                <td data-label="Aktion">
                  <div class="actions cost-actions">
                    <button
                      v-if="share.canReport"
                      :disabled="pending"
                      @click="reportPayment(entry.bill!.tripId, share)"
                    >
                      Als bezahlt melden
                    </button>
                    <button
                      v-if="share.canConfirm"
                      :disabled="pending"
                      @click="confirmPayment(entry.bill!.tripId, share.profileId)"
                    >
                      Zahlung bestätigen
                    </button>
                    <button
                      v-if="share.canReject"
                      class="danger"
                      :disabled="pending"
                      @click="rejectPayment(entry.bill!.tripId, share.profileId)"
                    >
                      Zahlung ablehnen
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="actions">
          <RouterLink class="button secondary" :to="`/fahrten/${entry.bill.tripId}`">
            Fahrtdetails und Abrechnung
          </RouterLink>
        </div>
      </template>
    </article>
  </section>
  <div v-if="nextOffset !== undefined" class="actions">
    <button class="secondary" :disabled="pending" @click="load(true)">
      {{ pending ? 'Wird geladen …' : 'Weitere Abrechnungen laden' }}
    </button>
  </div>
  <p v-if="loaded" class="hint cost-note">
    Zahlungen erfolgen außerhalb der App. Die Zahlungsmeldung überweist kein Geld.
  </p>
</template>

<style scoped>
.cost-summary {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 1.25rem;
  margin-bottom: 1.75rem;
}
.cost-total {
  margin: 0.2rem 0 0.7rem;
  font-size: clamp(1.7rem, 4vw, 2.25rem);
  font-weight: 750;
  letter-spacing: -0.03em;
  color: #17372c;
  overflow-wrap: anywhere;
}
.cost-note {
  margin-top: 1rem;
}
.cost-section-title h2 {
  margin-bottom: 0.35rem;
}
.cost-section-title p {
  margin-bottom: 0;
}
.cost-shares {
  margin-top: 1rem;
}
.cost-actions {
  margin-top: 0;
}
@media (min-width: 760px) {
  .cost-summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
