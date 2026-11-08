import { createClient } from '@connectrpc/connect'
import { Code, ConnectError, createGrpcTransport } from './grpc'
import { fetchApi } from './fetch'
import { markSessionExpired, sessionHeaders } from '../state/session'
import { apiBaseUrl } from './config'
import {
  BillingService,
  BookingService,
  DashboardService,
  ProfileService,
  RecurringTripService,
  TripService,
} from './generated/trips/v1/trips_pb'

export { apiBaseUrl } from './config'

/** Capture the explicit selection, so an in-flight action cannot switch people midway. */
export function clientsForPerson(personId: string) {
  if (!personId) throw new Error('Bitte zuerst eine Person auswählen.')
  const transport = createGrpcTransport({
    baseUrl: apiBaseUrl,
    defaultTimeoutMs: 15_000,
    getHeaders: () => sessionHeaders(personId),
    fetch: async (input, init) => {
      const response = await fetchApi(input, {
        ...init,
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (
        response.status === 403 &&
        response.headers.get('content-type')?.includes('application/json')
      ) {
        try {
          const body: unknown = await response.clone().json()
          if (
            body &&
            typeof body === 'object' &&
            'error' in body &&
            body.error === 'invalid_csrf_token'
          )
            markSessionExpired()
        } catch {
          // Preserve the original RPC response even if its diagnostic body is unreadable.
        }
      }
      return response
    },
    onError: (cause) => {
      if (cause instanceof ConnectError && cause.code === Code.Unauthenticated) markSessionExpired()
    },
  })
  return {
    trips: createClient(TripService, transport),
    profiles: createClient(ProfileService, transport),
    bookings: createClient(BookingService, transport),
    series: createClient(RecurringTripService, transport),
    billing: createClient(BillingService, transport),
    dashboard: createClient(DashboardService, transport),
  }
}
