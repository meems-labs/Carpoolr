import { recordApiReachability } from '../state/connection'

/** Browser online status can stay true on Wi-Fi without internet; observe actual API requests too. */
export const fetchApi: typeof globalThis.fetch = async (input, init) => {
  try {
    const response = await globalThis.fetch(input, init)
    recordApiReachability(true)
    return response
  } catch (cause) {
    const aborted =
      init?.signal?.aborted ||
      (typeof cause === 'object' &&
        cause !== null &&
        'name' in cause &&
        cause.name === 'AbortError')
    if (!aborted) recordApiReachability(false)
    throw cause
  }
}
