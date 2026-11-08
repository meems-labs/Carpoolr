import { readonly, ref } from 'vue'

const reachable = ref(true)
export const apiReachable = readonly(reachable)

export function recordApiReachability(value: boolean): void {
  reachable.value = value
}
