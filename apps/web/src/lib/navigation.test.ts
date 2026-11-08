import { describe, expect, it } from 'vitest'
import { activeNavigation, hasPersonalTripsNavigation, mainNavigation } from './navigation'

describe('application navigation', () => {
  it('has five distinct, directly reachable primary destinations', () => {
    expect(mainNavigation.map((item) => item.to)).toEqual([
      '/',
      '/fahrten',
      '/meine-fahrten',
      '/kosten',
      '/profil',
    ])
  })
  it.each(['/meine-fahrten', '/meine-anfragen', '/eingegangene-anfragen', '/fahrten/neu'])(
    'keeps the personal trip category active at %s',
    (path) => {
      expect(activeNavigation(path)).toBe('trips')
    },
  )
  it('keeps discovery details separate from creating an offer', () => {
    expect(activeNavigation('/fahrten')).toBe('search')
    expect(activeNavigation('/fahrten/11111111-1111-4111-8111-111111111111')).toBe('search')
    expect(activeNavigation('/fahrten/neu')).toBe('trips')
  })
  it('shows the three-way subnavigation only on related list pages', () => {
    expect(hasPersonalTripsNavigation('/meine-fahrten')).toBe(true)
    expect(hasPersonalTripsNavigation('/meine-anfragen')).toBe(true)
    expect(hasPersonalTripsNavigation('/eingegangene-anfragen')).toBe(true)
    expect(hasPersonalTripsNavigation('/fahrten/neu')).toBe(false)
    expect(hasPersonalTripsNavigation('/kosten')).toBe(false)
  })
})
