export type ActivationStatus = 'checking' | 'blocked' | 'activating' | 'error'

interface UpdateClient {
  id: string
}

export interface UpdateGateOptions<TClient extends UpdateClient> {
  listClients(): Promise<readonly TClient[]>
  prepareClient(client: TClient): Promise<boolean>
  cancelClients(clients: readonly TClient[]): Promise<void>
  activate(): Promise<void>
}

/** All tabs must freeze their UI and acknowledge safety before an update can take over. */
export function createUpdateGate<TClient extends UpdateClient>(
  options: UpdateGateOptions<TClient>,
) {
  let activationInProgress = false

  return async (reply: (status: ActivationStatus) => void): Promise<void> => {
    if (activationInProgress) {
      reply('checking')
      return
    }
    activationInProgress = true
    let preparedClients: readonly TClient[] = []

    async function cancelPreparation() {
      let clients = preparedClients
      try {
        const currentClients = await options.listClients()
        clients = [
          ...new Map(
            [...preparedClients, ...currentClients].map((client) => [client.id, client]),
          ).values(),
        ]
      } catch {
        // Already-known tabs still need to unlock if client enumeration fails.
      }
      await options.cancelClients(clients).catch(() => undefined)
    }

    try {
      preparedClients = await options.listClients()
      const acknowledgements = await Promise.all(
        preparedClients.map((client) => options.prepareClient(client)),
      )
      const preparedIds = new Set(preparedClients.map((client) => client.id))
      const currentClients = await options.listClients()
      const newTabAppeared = currentClients.some((client) => !preparedIds.has(client.id))

      if (!acknowledgements.every((safe) => safe === true) || newTabAppeared) {
        await cancelPreparation()
        activationInProgress = false
        reply('blocked')
        return
      }

      reply('activating')
      await options.activate()
      // Keep the gate closed after success; controllerchange will reload the prepared tabs.
    } catch {
      await cancelPreparation()
      activationInProgress = false
      reply('error')
    }
  }
}
