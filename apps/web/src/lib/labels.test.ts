import { describe, expect, it } from 'vitest'
import { formatCents } from './labels'

describe('EUR cent formatting', () => {
  it('keeps large int64 amounts exact rather than converting them to floating point', () => {
    expect(formatCents(123456789012345678901n)).toBe('1.234.567.890.123.456.789,01 €')
    expect(formatCents(1n)).toBe('0,01 €')
    expect(formatCents(0n)).toBe('0,00 €')
  })
})
