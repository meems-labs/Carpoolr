/** API root; browser sign-in requires the same-origin /api deployment. */
export const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'
