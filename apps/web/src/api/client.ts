// Typed fetch wrapper for the Carpoolr BFF (apps/api).
// Direct fetch + CORS — deliberately NO Vite dev proxy, so dev and prod
// resolve the base URL identically via VITE_API_BASE_URL.
const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'

export interface Health {
  status: string
}

export async function health(): Promise<Health> {
  const res = await fetch(`${API_BASE_URL}/healthz`)
  if (!res.ok) {
    throw new Error(`GET /healthz failed: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as Health
}