import { createClient } from '@connectrpc/connect'
import { createGrpcTransport } from './grpc'
import {
  BillingService,
  BookingService,
  ProfileService,
  RecurringTripService,
  TripService,
} from './generated/trips/v1/trips_pb'

export const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'

/** Capture the explicit selection, so an in-flight action cannot switch people midway. */
export function clientsForPerson(personId: string) {
  if (!personId) throw new Error('Bitte zuerst eine Person auswählen.')
  const transport = createGrpcTransport({
    baseUrl: apiBaseUrl,
    defaultTimeoutMs: 15_000,
    getHeaders: () => ({ 'X-Carpoolr-Demo-User': personId }),
  })
  return {
    trips: createClient(TripService, transport),
    profiles: createClient(ProfileService, transport),
    bookings: createClient(BookingService, transport),
    series: createClient(RecurringTripService, transport),
    billing: createClient(BillingService, transport),
  }
}
