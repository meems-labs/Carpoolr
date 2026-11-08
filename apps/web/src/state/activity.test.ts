import { afterEach, describe, expect, it } from 'vitest'
import { effectScope, reactive, ref, type EffectScope } from 'vue'
import { useOperation } from '../composables/useOperation'
import { useUnsavedChanges } from '../composables/useUnsavedChanges'
import {
  activeOperationCount,
  beginOperation,
  canReloadFromActivity,
  dirtyFormCount,
} from './activity'

const scopes: EffectScope[] = []

function inScope<T>(setup: () => T) {
  const scope = effectScope()
  scopes.push(scope)
  return { scope, value: scope.run(setup)! }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

afterEach(() => {
  for (const scope of scopes.splice(0)) scope.stop()
  expect(activeOperationCount.value).toBe(0)
  expect(dirtyFormCount.value).toBe(0)
  expect(canReloadFromActivity.value).toBe(true)
})

describe('safe automatic update activity', () => {
  it('blocks immediately for nested edits and becomes safe again when the values are restored', () => {
    const form = reactive({ address: 'Original address', recurrence: { weekdays: [1, 3] } })
    const { value: changes } = inScope(() => useUnsavedChanges(() => form))
    expect(changes.isDirty.value).toBe(false)
    expect(canReloadFromActivity.value).toBe(true)

    form.recurrence.weekdays.push(5)
    expect(changes.isDirty.value).toBe(true)
    expect(dirtyFormCount.value).toBe(1)
    expect(canReloadFromActivity.value).toBe(false)
    form.address = 'Changed address'
    expect(dirtyFormCount.value).toBe(1)

    form.recurrence.weekdays.pop()
    form.address = 'Original address'
    expect(changes.isDirty.value).toBe(false)
    expect(canReloadFromActivity.value).toBe(true)
  })

  it('tracks independent forms and establishes new baselines only when explicitly saved or loaded', () => {
    const first = ref('Initial profile')
    const second = ref(false)
    const { value: firstChanges } = inScope(() => useUnsavedChanges(() => first.value))
    const { value: secondChanges } = inScope(() => useUnsavedChanges(() => second.value))
    first.value = 'Server-normalized profile'
    second.value = true
    expect(dirtyFormCount.value).toBe(2)

    firstChanges.markSaved()
    expect(dirtyFormCount.value).toBe(1)
    expect(canReloadFromActivity.value).toBe(false)
    first.value = 'Initial profile'
    expect(firstChanges.isDirty.value).toBe(true)
    first.value = 'Server-normalized profile'
    expect(firstChanges.isDirty.value).toBe(false)
    secondChanges.markSaved()
    expect(canReloadFromActivity.value).toBe(true)
  })

  it('releases a dirty form when its view is disposed and ignores later stale callbacks', () => {
    const form = reactive({ distance: '25', consumption: '6.5', price: '1.8' })
    const { scope, value: changes } = inScope(() => useUnsavedChanges(() => ({ ...form })))
    form.price = '2'
    expect(dirtyFormCount.value).toBe(1)

    scope.stop()
    expect(dirtyFormCount.value).toBe(0)
    form.distance = '50'
    changes.markSaved()
    scope.stop()
    expect(dirtyFormCount.value).toBe(0)
  })

  it('requires both settled operations and saved input before permitting reload', () => {
    const form = ref('Original')
    const { value: changes } = inScope(() => useUnsavedChanges(() => form.value))
    form.value = 'Edited'
    const firstFinished = beginOperation()
    const secondFinished = beginOperation()
    expect(activeOperationCount.value).toBe(2)
    changes.markSaved()
    expect(canReloadFromActivity.value).toBe(false)

    firstFinished()
    firstFinished()
    expect(activeOperationCount.value).toBe(1)
    secondFinished()
    secondFinished()
    expect(canReloadFromActivity.value).toBe(true)
  })

  it('keeps overlapping view operations blocked until both settle and ignores duplicate submissions', async () => {
    const first = deferred<string>()
    const second = deferred<string>()
    const { value: firstView } = inScope(useOperation)
    const { value: secondView } = inScope(useOperation)
    const firstCall = firstView.run(() => first.promise)
    const secondCall = secondView.run(() => second.promise)
    let duplicateCalled = false
    await firstView.run(async () => {
      duplicateCalled = true
      return 'duplicate'
    })
    expect(duplicateCalled).toBe(false)
    expect(activeOperationCount.value).toBe(2)

    first.resolve('first saved')
    await expect(firstCall).resolves.toBe('first saved')
    expect(activeOperationCount.value).toBe(1)
    expect(canReloadFromActivity.value).toBe(false)
    second.resolve('second saved')
    await secondCall
    expect(activeOperationCount.value).toBe(0)
  })

  it('leaves failed saves dirty while releasing their operation blocker', async () => {
    const form = ref('Original')
    const { value: view } = inScope(() => ({
      changes: useUnsavedChanges(() => form.value),
      operation: useOperation(),
    }))
    form.value = 'Unsaved input'

    await view.operation.run(async () => {
      throw new Error('Save failed')
    })

    expect(view.operation.error.value).toBe('Save failed')
    expect(view.changes.isDirty.value).toBe(true)
    expect(activeOperationCount.value).toBe(0)
    expect(canReloadFromActivity.value).toBe(false)
  })

  it('retains the operation blocker after disposal until abort cleanup actually completes', async () => {
    const completion = deferred<string>()
    const { scope, value: operation } = inScope(useOperation)
    let aborted = false
    const call = operation.run(({ signal }) => {
      signal?.addEventListener('abort', () => {
        aborted = true
      })
      return completion.promise
    })

    scope.stop()
    expect(aborted).toBe(true)
    expect(activeOperationCount.value).toBe(1)
    expect(canReloadFromActivity.value).toBe(false)
    completion.reject(new Error('Abort cleanup finished'))
    await call
    expect(activeOperationCount.value).toBe(0)
    expect(operation.error.value).toBe('')
    await expect(operation.run(async () => 'stale call')).resolves.toBeUndefined()
  })
})
