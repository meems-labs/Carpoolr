import { describe, expect, it } from 'vitest'
import { berlinDateTime, berlinDay, decimal, localDateTime, normalizeAddress } from './format'

describe('Berlin wall-clock times', () => {
  it('converts winter and summer times independently of the browser timezone', () => {
    expect(berlinDateTime('2027-01-12T07:30').toISOString()).toBe('2027-01-12T06:30:00.000Z')
    expect(berlinDateTime('2027-07-12T07:30').toISOString()).toBe('2027-07-12T05:30:00.000Z')
    expect(localDateTime(new Date('2027-07-12T05:30:00Z'))).toBe('2027-07-12T07:30')
  })

  it('rejects nonexistent and ambiguous DST wall-clock times', () => {
    expect(() => berlinDateTime('2027-03-28T02:30')).toThrow('existiert')
    expect(() => berlinDateTime('2027-10-31T02:30')).toThrow('doppeldeutig')
  })

  it('uses calendar days across DST changes, rather than assuming 24 hours', () => {
    const spring = berlinDay('2027-03-28')
    const autumn = berlinDay('2027-10-31')
    expect((spring.until.getTime() - spring.from.getTime()) / 3_600_000).toBe(23)
    expect((autumn.until.getTime() - autumn.from.getTime()) / 3_600_000).toBe(25)
  })

  it('rejects dates normalized by JavaScript into another calendar day', () => {
    expect(() => berlinDateTime('2027-02-30T08:00')).toThrow('existiert')
  })
})

describe('decimal wire values', () => {
  it('preserves the decimal digits without floating-point arithmetic', () => {
    expect(decimal(' 1,859 ')).toBe('1.859')
    expect(decimal('0.1234')).toBe('0.1234')
    expect(decimal('001.2300')).toBe('1.23')
  })

  it('rejects nonpositive values, exponential notation and excess precision', () => {
    for (const value of ['0', '-1', '1e3', '1.12345', 'NaN', '1,2,3']) {
      expect(() => decimal(value)).toThrow()
    }
  })

  it('checks the server bounds and normalizes multiline addresses', () => {
    expect(() => decimal('100.01', 100)).toThrow('100')
    expect(normalizeAddress(' Straße 1\n\t12345  Stadt ')).toBe('Straße 1 12345 Stadt')
  })
})
