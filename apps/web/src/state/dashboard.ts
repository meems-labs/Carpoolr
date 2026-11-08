import { ref } from 'vue'

// Views notify the header after a successful mutation; the server remains the source of truth.
export const dashboardRefreshVersion = ref(0)

export function requestDashboardRefresh(): void {
  dashboardRefreshVersion.value++
}
