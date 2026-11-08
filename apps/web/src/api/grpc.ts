import { createClient, type Client, type Interceptor, type Transport } from '@connectrpc/connect'
import { createGrpcWebTransport } from '@connectrpc/connect-web'
import { TripService } from './generated/trips/v1/trips_pb'

export interface GrpcClientOptions {
  /** API root, including a proxy prefix such as /api; never include the RPC path. */
  baseUrl?: string
  /** Called for every RPC, so a caller can supply a refreshed token without rebuilding the client. */
  getAccessToken?: (signal: AbortSignal) => string | undefined | Promise<string | undefined>
  /** Additional per-call metadata. Explicit call-option headers take precedence. */
  getHeaders?: (signal: AbortSignal) => HeadersInit | undefined | Promise<HeadersInit | undefined>
  /** Default deadline; individual calls can override it through timeoutMs. */
  defaultTimeoutMs?: number
  /** Optional fetch override, for tests or application-specific browser fetch settings. */
  fetch?: typeof globalThis.fetch
  /** Observe failures without retrying RPCs or changing their standard error codes. */
  onError?: (cause: unknown) => void
}

export type TripClient = Client<typeof TripService>

/**
 * Creates a reusable typed client for ASP.NET Core's binary gRPC-Web endpoints.
 * Identity/session handling stays with the caller; no token or person is stored here.
 */
export function createTripClient(options: GrpcClientOptions = {}): TripClient {
  return createClient(TripService, createGrpcTransport(options))
}

/** A shared transport lets all generated services use identical metadata and deadline handling. */
export function createGrpcTransport(options: GrpcClientOptions = {}): Transport {
  const metadata: Interceptor = (next) => async (request) => {
    request.signal.throwIfAborted()

    const providedHeaders = await awaitMetadata(
      options.getHeaders?.(request.signal),
      request.signal,
    )
    new Headers(providedHeaders).forEach((value, name) => {
      if (!request.header.has(name)) request.header.set(name, value)
    })

    const token = await awaitMetadata(options.getAccessToken?.(request.signal), request.signal)
    if (token && !request.header.has('authorization')) {
      request.header.set('authorization', `Bearer ${token}`)
    }

    request.signal.throwIfAborted()
    try {
      return await next(request)
    } catch (cause) {
      options.onError?.(cause)
      throw cause
    }
  }

  return createGrpcWebTransport({
    baseUrl: options.baseUrl ?? import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000',
    useBinaryFormat: true,
    interceptors: [metadata],
    defaultTimeoutMs: options.defaultTimeoutMs,
    fetch: options.fetch,
  })
}

/** Stop waiting on metadata when the call is cancelled, even if its provider ignores the signal. */
async function awaitMetadata<T>(value: T | Promise<T>, signal: AbortSignal): Promise<T> {
  signal.throwIfAborted()
  let onAbort: () => void = () => {}
  const aborted = new Promise<never>((_, reject) => {
    onAbort = () => reject(signal.reason)
    signal.addEventListener('abort', onAbort, { once: true })
  })

  try {
    return await Promise.race([value, aborted])
  } finally {
    signal.removeEventListener('abort', onAbort)
  }
}

// Preserve the library's standard gRPC status codes and cancellation/deadline behavior.
export { Code, ConnectError } from '@connectrpc/connect'
export type { CallOptions } from '@connectrpc/connect'
