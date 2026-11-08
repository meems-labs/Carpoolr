import { create, fromBinary, toBinary } from '@bufbuild/protobuf'
import { describe, expect, it, vi } from 'vitest'
import { Code, ConnectError, createTripClient } from './grpc'
import { GetTripRequestSchema, GetTripResponseSchema } from './generated/trips/v1/trips_pb'

const tripId = '266e7d76-2484-4266-9f75-07421f2f7e5f'

describe('typed gRPC-Web transport', () => {
  it('uses the proxy prefix and binary gRPC-Web framing, and reads protobuf responses', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(success())
    const client = createTripClient({ baseUrl: 'https://carpoolr.test/api', fetch: fetchMock })

    const result = await client.getTrip({ tripId })

    expect(result.trip?.id).toBe(tripId)
    expect(fetchMock).toHaveBeenCalledOnce()
    const [input, init] = fetchMock.mock.calls[0]
    expect(input).toBe('https://carpoolr.test/api/carpoolr.trips.v1.TripService/GetTrip')
    expect(init?.method).toBe('POST')
    const headers = new Headers(init?.headers)
    expect(headers.get('content-type')).toBe('application/grpc-web+proto')
    expect(headers.has('authorization')).toBe(false)

    const bytes = new Uint8Array(await new Response(init?.body).arrayBuffer())
    expect(bytes[0]).toBe(0)
    expect(new DataView(bytes.buffer).getUint32(1)).toBe(bytes.length - 5)
    expect(fromBinary(GetTripRequestSchema, bytes.subarray(5)).tripId).toBe(tripId)
  })

  it('resolves asynchronous tokens and metadata separately for every call', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () => success())
    const tokens = ['first-token', 'refreshed-token']
    const getAccessToken = vi.fn(async (signal: AbortSignal) => {
      expect(signal.aborted).toBe(false)
      return tokens.shift()
    })
    const client = createTripClient({
      baseUrl: 'https://carpoolr.test',
      fetch: fetchMock,
      getAccessToken,
      getHeaders: async () => ({ 'x-correlation-id': 'test-call' }),
    })

    await client.getTrip({ tripId })
    await client.getTrip({ tripId })

    expect(getAccessToken).toHaveBeenCalledTimes(2)
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('authorization')).toBe(
      'Bearer first-token',
    )
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('authorization')).toBe(
      'Bearer refreshed-token',
    )
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('x-correlation-id')).toBe(
      'test-call',
    )
  })

  it('keeps explicit call metadata ahead of provider defaults', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(success())
    const client = createTripClient({
      baseUrl: 'https://carpoolr.test',
      fetch: fetchMock,
      getAccessToken: () => 'provider-token',
      getHeaders: () => ({ 'x-correlation-id': 'provider-id' }),
    })

    await client.getTrip(
      { tripId },
      {
        headers: { authorization: 'Bearer explicit-token', 'x-correlation-id': 'explicit-id' },
      },
    )

    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers)
    expect(headers.get('authorization')).toBe('Bearer explicit-token')
    expect(headers.get('x-correlation-id')).toBe('explicit-id')
  })

  it('preserves gRPC error status and metadata from trailers', async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValue(
        grpcResponse([
          frame(
            0x80,
            new TextEncoder().encode(
              'grpc-status: 5\r\ngrpc-message: Trip%20not%20found\r\ntrace-id: test-trace\r\n',
            ),
          ),
        ]),
      )
    const client = createTripClient({ baseUrl: 'https://carpoolr.test', fetch: fetchMock })

    const error = await client.getTrip({ tripId }).catch((cause: unknown) => cause)

    expect(error).toBeInstanceOf(ConnectError)
    expect(error).toMatchObject({ code: Code.NotFound, rawMessage: 'Trip not found' })
    expect((error as ConnectError).metadata.get('trace-id')).toBe('test-trace')
  })

  it('honors a caller abort signal before sending a request', async () => {
    const fetchMock = vi.fn<typeof fetch>()
    const client = createTripClient({ baseUrl: 'https://carpoolr.test', fetch: fetchMock })
    const abort = new AbortController()
    abort.abort()

    await expect(client.getTrip({ tripId }, { signal: abort.signal })).rejects.toMatchObject({
      code: Code.Canceled,
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('stops waiting for asynchronous metadata when the caller cancels', async () => {
    const fetchMock = vi.fn<typeof fetch>()
    let started: () => void = () => {}
    const providerStarted = new Promise<void>((resolve) => {
      started = resolve
    })
    const client = createTripClient({
      baseUrl: 'https://carpoolr.test',
      fetch: fetchMock,
      getAccessToken: () => {
        started()
        return new Promise<string>(() => {})
      },
    })
    const abort = new AbortController()
    const pending = client.getTrip({ tripId }, { signal: abort.signal })
    const rejected = expect(pending).rejects.toMatchObject({ code: Code.Canceled })

    await providerStarted
    abort.abort()

    await rejected
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('applies per-call deadlines while metadata is still pending', async () => {
    const fetchMock = vi.fn<typeof fetch>()
    const client = createTripClient({
      baseUrl: 'https://carpoolr.test',
      fetch: fetchMock,
      defaultTimeoutMs: 10_000,
      getHeaders: () => new Promise<HeadersInit>(() => {}),
    })

    await expect(client.getTrip({ tripId }, { timeoutMs: 20 })).rejects.toMatchObject({
      code: Code.DeadlineExceeded,
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

function success(): Response {
  const message = create(GetTripResponseSchema, { trip: { id: tripId } })
  return grpcResponse([
    frame(0, toBinary(GetTripResponseSchema, message)),
    frame(0x80, new TextEncoder().encode('grpc-status: 0\r\n')),
  ])
}

function frame(flags: number, payload: Uint8Array): Uint8Array {
  const data = new Uint8Array(5 + payload.length)
  data[0] = flags
  new DataView(data.buffer).setUint32(1, payload.length)
  data.set(payload, 5)
  return data
}

function grpcResponse(frames: Uint8Array[]): Response {
  const data = new Uint8Array(frames.reduce((total, current) => total + current.length, 0))
  let offset = 0
  for (const current of frames) {
    data.set(current, offset)
    offset += current.length
  }
  return new Response(data, { headers: { 'content-type': 'application/grpc-web+proto' } })
}
