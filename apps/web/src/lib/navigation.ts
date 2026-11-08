export const mainNavigation = [
  { id: 'home', to: '/', label: 'Start', icon: 'home' },
  { id: 'search', to: '/fahrten', label: 'Suchen', icon: 'search' },
  { id: 'trips', to: '/meine-fahrten', label: 'Meine Fahrten', icon: 'car' },
  { id: 'costs', to: '/kosten', label: 'Kosten', icon: 'wallet' },
  { id: 'profile', to: '/profil', label: 'Profil', icon: 'user' },
] as const

export const personalTripsNavigation = [
  { to: '/meine-fahrten', label: 'Angebote' },
  { to: '/meine-anfragen', label: 'Mitfahrten' },
  { to: '/eingegangene-anfragen', label: 'Anfragen' },
] as const

export function activeNavigation(path: string): (typeof mainNavigation)[number]['id'] | undefined {
  if (path === '/') return 'home'
  if (path === '/fahrten/neu' || personalTripsNavigation.some((item) => item.to === path))
    return 'trips'
  if (path === '/fahrten' || path.startsWith('/fahrten/')) return 'search'
  if (path === '/kosten') return 'costs'
  if (path === '/profil') return 'profile'
  return undefined
}

export function hasPersonalTripsNavigation(path: string): boolean {
  return personalTripsNavigation.some((item) => item.to === path)
}
