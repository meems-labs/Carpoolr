import { describe, expect, it, vi } from 'vitest'
import { createUpdateGate, type ActivationStatus } from './workerUpdateGate'

function setup() {
  const clients = [{ id: 'first-tab' }, { id: 'second-tab' }]
  const options = {
    listClients: vi.fn(async () => clients),
    prepareClient: vi.fn(async (_client: { id: string }) => true),
    cancelClients: vi.fn(async (_clients: readonly { id: string }[]) => undefined),
    activate: vi.fn(async () => undefined),
  }
  return { clients, options, activate: createUpdateGate(options) }
}

describe('waiting-worker activation gate', () => {
  it('activates only after all tabs acknowledge safety and the client list is checked again', async () => {
    const { clients, options, activate } = setup()
    const replies: ActivationStatus[] = []

    await activate((status) => replies.push(status))

    expect(options.prepareClient).toHaveBeenCalledTimes(clients.length)
    expect(options.listClients).toHaveBeenCalledTimes(2)
    expect(options.activate).toHaveBeenCalledOnce()
    expect(options.cancelClients).not.toHaveBeenCalled()
    expect(replies).toEqual(['activating'])
  })

  it('leaves the worker waiting and unlocks all tabs if one tab is busy or fails to acknowledge', async () => {
    const { clients, options, activate } = setup()
    options.prepareClient.mockResolvedValueOnce(false)
    const replies: ActivationStatus[] = []

    await activate((status) => replies.push(status))

    expect(options.activate).not.toHaveBeenCalled()
    expect(options.cancelClients).toHaveBeenCalledWith(clients)
    expect(replies).toEqual(['blocked'])
  })

  it('blocks if a new, unprepared tab appears during the handshake', async () => {
    const { clients, options, activate } = setup()
    const newClients = [...clients, { id: 'new-tab' }]
    options.listClients.mockResolvedValueOnce(clients).mockResolvedValue(newClients)
    const replies: ActivationStatus[] = []

    await activate((status) => replies.push(status))

    expect(options.activate).not.toHaveBeenCalled()
    expect(options.cancelClients).toHaveBeenCalledWith(newClients)
    expect(replies).toEqual(['blocked'])
  })

  it('does not block merely because a prepared tab was closed', async () => {
    const { clients, options, activate } = setup()
    options.listClients.mockResolvedValueOnce(clients).mockResolvedValue([clients[0]!])

    await activate(() => undefined)

    expect(options.activate).toHaveBeenCalledOnce()
  })

  it('serializes competing activation requests into one handshake', async () => {
    const { options, activate } = setup()
    const acknowledgements: ((safe: boolean) => void)[] = []
    options.prepareClient.mockImplementation(
      () => new Promise<boolean>((resolve) => acknowledgements.push(resolve)),
    )
    const firstReplies: ActivationStatus[] = []
    const secondReplies: ActivationStatus[] = []
    const first = activate((status) => firstReplies.push(status))
    await Promise.resolve()

    await activate((status) => secondReplies.push(status))
    expect(secondReplies).toEqual(['checking'])
    expect(options.activate).not.toHaveBeenCalled()
    expect(acknowledgements).toHaveLength(2)

    acknowledgements.forEach((acknowledge) => acknowledge(true))
    await first
    expect(firstReplies).toEqual(['activating'])
    expect(options.activate).toHaveBeenCalledOnce()
  })

  it('cancels preparation on errors and permits a safe retry', async () => {
    const { clients, options, activate } = setup()
    options.activate.mockRejectedValueOnce(new Error('activation unavailable'))
    const replies: ActivationStatus[] = []

    await activate((status) => replies.push(status))
    expect(replies).toEqual(['activating', 'error'])
    expect(options.cancelClients).toHaveBeenCalledWith(clients)

    await activate((status) => replies.push(status))
    expect(replies).toEqual(['activating', 'error', 'activating'])
    expect(options.activate).toHaveBeenCalledTimes(2)
  })

  it('never activates when listing clients fails', async () => {
    const { options, activate } = setup()
    options.listClients.mockRejectedValueOnce(new Error('clients unavailable'))
    const replies: ActivationStatus[] = []

    await activate((status) => replies.push(status))

    expect(options.activate).not.toHaveBeenCalled()
    expect(options.cancelClients).toHaveBeenCalledOnce()
    expect(replies).toEqual(['error'])
  })
})
