import { timestampDate, type Timestamp } from '@bufbuild/protobuf/wkt'

export const timeZone = 'Europe/Berlin'

const berlinParts = new Intl.DateTimeFormat('en-CA', {
  timeZone,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

export function localDateTime(value: Date = new Date()): string {
  const parts = berlinParts.formatToParts(value)
  const part = (name: Intl.DateTimeFormatPartTypes) =>
    parts.find((entry) => entry.type === name)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}`
}

/** Convert a Berlin wall-clock input without relying on the browser's own timezone. */
export function berlinDateTime(value: string): Date {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)) {
    throw new Error('Bitte ein vollständiges Datum und eine Uhrzeit eingeben.')
  }
  const approximate = Date.parse(`${value}:00Z`)
  if (!Number.isFinite(approximate)) throw new Error('Das Datum ist ungültig.')

  const matches = [-120, -60, 0, 60, 120]
    .map((minutes) => new Date(approximate + minutes * 60_000))
    .filter((candidate) => localDateTime(candidate) === value)
  if (matches.length === 0) {
    throw new Error(
      'Diese Uhrzeit existiert wegen der Zeitumstellung nicht. Bitte eine andere Uhrzeit wählen.',
    )
  }
  if (matches.length > 1) {
    throw new Error(
      'Diese Uhrzeit ist wegen der Zeitumstellung doppeldeutig. Bitte eine eindeutige Uhrzeit wählen.',
    )
  }
  return matches[0]
}

export function berlinDay(value: string): { from: Date; until: Date } {
  const from = berlinDateTime(`${value}T00:00`)
  const nextCalendarDay = new Date(`${value}T12:00:00Z`)
  nextCalendarDay.setUTCDate(nextCalendarDay.getUTCDate() + 1)
  return { from, until: berlinDateTime(`${nextCalendarDay.toISOString().slice(0, 10)}T00:00`) }
}

export function formatDate(value?: Timestamp): string {
  if (!value) return '—'
  return new Intl.DateTimeFormat('de-DE', {
    timeZone,
    weekday: 'short',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(timestampDate(value))
}

export function decimal(value: string, maximum?: number): string {
  const normalized = value.trim().replace(',', '.')
  if (
    !/^\d+(?:\.\d{1,4})?$/.test(normalized) ||
    !/[1-9]/.test(normalized) ||
    normalized.length > 20
  ) {
    throw new Error(
      'Bitte einen positiven Zahlenwert mit höchstens vier Nachkommastellen eingeben.',
    )
  }
  if (maximum !== undefined && Number(normalized) > maximum)
    throw new Error(`Der Wert darf höchstens ${maximum} betragen.`)
  return normalized
    .replace(/^0+(?=\d)/, '')
    .replace(/(\.\d*?)0+$/, '$1')
    .replace(/\.$/, '')
}

export function normalizeAddress(value: string): string {
  return value.trim().replace(/\s+/g, ' ')
}

export function formatDecimal(value: string | undefined): string {
  return value ? value.replace('.', ',') : '—'
}
