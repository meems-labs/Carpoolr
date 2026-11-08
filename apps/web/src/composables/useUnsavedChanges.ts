import { getCurrentScope, onScopeDispose, readonly, ref, watch } from 'vue'
import { registerDirtyForm } from '../state/activity'

/** Track JSON-compatible editable values against the last successfully saved or loaded baseline. */
export function useUnsavedChanges(values: () => unknown) {
  if (!getCurrentScope()) throw new Error('useUnsavedChanges requires an active Vue scope.')

  const registration = registerDirtyForm()
  const isDirty = ref(false)
  const snapshot = () => JSON.stringify(values())
  let baseline = snapshot()
  let disposed = false
  const stop = watch(
    snapshot,
    (current) => {
      isDirty.value = current !== baseline
      registration.setDirty(isDirty.value)
    },
    // A worker activation handshake must see edits before another event can request a reload.
    { flush: 'sync' },
  )

  function markSaved() {
    if (disposed) return
    baseline = snapshot()
    isDirty.value = false
    registration.setDirty(false)
  }

  onScopeDispose(() => {
    disposed = true
    stop()
    registration.dispose()
    isDirty.value = false
  })

  return { isDirty: readonly(isDirty), markSaved }
}
