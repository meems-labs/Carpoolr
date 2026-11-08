import { onScopeDispose, ref } from 'vue'
import { Code, ConnectError, type CallOptions } from '../api/grpc'
import { beginOperation } from '../state/activity'

export function errorMessage(cause: unknown): string {
  if (cause instanceof TypeError && /fetch|network|load failed/i.test(cause.message))
    return 'Die Verbindung zum Server konnte nicht hergestellt werden. Bitte erneut versuchen.'
  if (cause instanceof ConnectError) {
    if (cause.code === Code.Unavailable)
      return 'Der Server ist gerade nicht erreichbar. Bitte erneut versuchen.'
    if (cause.code === Code.DeadlineExceeded)
      return 'Die Anfrage dauert zu lange. Bitte erneut versuchen.'
    if (cause.code === Code.PermissionDenied)
      return 'Diese Aktion ist für die ausgewählte Person nicht erlaubt.'
    if (cause.code === Code.Unauthenticated)
      return 'Die ausgewählte Testperson wird vom Server nicht akzeptiert.'
    return cause.rawMessage || 'Die Anfrage konnte nicht ausgeführt werden.'
  }
  return cause instanceof Error ? cause.message : 'Ein unerwarteter Fehler ist aufgetreten.'
}

export function useOperation() {
  const pending = ref(false)
  const error = ref('')
  const success = ref('')
  const controllers = new Set<AbortController>()
  let mounted = true

  onScopeDispose(() => {
    mounted = false
    for (const controller of controllers) controller.abort()
  })

  async function run<T>(operation: (options: CallOptions) => Promise<T>): Promise<T | undefined> {
    if (!mounted || pending.value) return undefined
    const controller = new AbortController()
    controllers.add(controller)
    const finish = beginOperation()
    pending.value = true
    error.value = ''
    success.value = ''
    try {
      return await operation({ signal: controller.signal, timeoutMs: 15_000 })
    } catch (cause) {
      if (mounted && !(cause instanceof ConnectError && cause.code === Code.Canceled)) {
        error.value = errorMessage(cause)
      }
      return undefined
    } finally {
      controllers.delete(controller)
      if (mounted) pending.value = false
      finish()
    }
  }

  return { pending, error, success, run }
}
