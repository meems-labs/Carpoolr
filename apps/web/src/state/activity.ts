import { computed, readonly, ref } from 'vue'

const operations = ref(0)
const dirtyForms = ref(0)

export const activeOperationCount = readonly(operations)
export const dirtyFormCount = readonly(dirtyForms)
export const canReloadFromActivity = computed(
  () => operations.value === 0 && dirtyForms.value === 0,
)

/** Keep reloads blocked until the entire operation has settled, including abort cleanup. */
export function beginOperation(): () => void {
  operations.value++
  let finished = false
  return () => {
    if (finished) return
    finished = true
    operations.value--
  }
}

/** A form contributes one blocker regardless of how many fields have changed. */
export function registerDirtyForm(): { setDirty: (value: boolean) => void; dispose: () => void } {
  let dirty = false
  let disposed = false
  function setDirty(value: boolean) {
    if (disposed || value === dirty) return
    dirty = value
    dirtyForms.value += value ? 1 : -1
  }
  return {
    setDirty,
    dispose() {
      if (disposed) return
      setDirty(false)
      disposed = true
    },
  }
}
