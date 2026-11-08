import {
  BookingState,
  EnergyKind,
  PaymentState,
  TripState,
} from '../api/generated/trips/v1/trips_pb'

export const weekdays = [
  { value: 1, label: 'Mo' },
  { value: 2, label: 'Di' },
  { value: 3, label: 'Mi' },
  { value: 4, label: 'Do' },
  { value: 5, label: 'Fr' },
  { value: 6, label: 'Sa' },
  { value: 7, label: 'So' },
]

export function tripLabel(state: TripState): string {
  return (
    {
      [TripState.UNSPECIFIED]: 'Unbekannt',
      [TripState.PLANNED]: 'Geplant',
      [TripState.CANCELLED]: 'Abgesagt',
      [TripState.COMPLETED]: 'Abgeschlossen',
    }[state] ?? 'Unbekannt'
  )
}

export function bookingLabel(state: BookingState): string {
  return (
    {
      [BookingState.UNSPECIFIED]: 'Unbekannt',
      [BookingState.PENDING]: 'Angefragt',
      [BookingState.ACCEPTED]: 'Zugesagt',
      [BookingState.REJECTED]: 'Abgelehnt',
      [BookingState.WITHDRAWN]: 'Zurückgezogen',
      [BookingState.CANCELLED]: 'Fahrt abgesagt',
    }[state] ?? 'Unbekannt'
  )
}

export function paymentLabel(state: PaymentState): string {
  return (
    {
      [PaymentState.UNSPECIFIED]: 'Unbekannt',
      [PaymentState.UNPAID]: 'Offen',
      [PaymentState.REPORTED]: 'Als bezahlt gemeldet',
      [PaymentState.CONFIRMED]: 'Zahlung bestätigt',
      [PaymentState.REJECTED]: 'Zahlung nicht bestätigt',
      [PaymentState.DRIVER_SHARE]: 'Eigener Fahreranteil',
    }[state] ?? 'Unbekannt'
  )
}

export function energyLabel(kind: EnergyKind): string {
  return (
    {
      [EnergyKind.UNSPECIFIED]: '—',
      [EnergyKind.PETROL]: 'Benzin',
      [EnergyKind.DIESEL]: 'Diesel',
      [EnergyKind.ELECTRIC]: 'Strom',
    }[kind] ?? '—'
  )
}

export function unitLabel(kind: EnergyKind): string {
  return kind === EnergyKind.ELECTRIC ? 'kWh' : 'l'
}

export function formatCents(value: bigint | undefined): string {
  if (value === undefined) return '—'
  const amount = value < 0n ? -value : value
  return `${value < 0n ? '−' : ''}${(amount / 100n).toLocaleString('de-DE')},${(amount % 100n).toString().padStart(2, '0')} €`
}
