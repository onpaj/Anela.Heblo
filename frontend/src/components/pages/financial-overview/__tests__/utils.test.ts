import { formatCurrency, getPeriodLabel, MONTH_SLOT_WIDTH } from '../utils'

describe('formatCurrency', () => {
  it('formats a positive integer amount', () => {
    expect(formatCurrency(1000)).toBe('1 000 Kč')
  })

  it('formats zero as 0 Kč', () => {
    expect(formatCurrency(0)).toBe('0 Kč')
  })

  it('formats a negative amount with leading minus sign', () => {
    expect(formatCurrency(-5000)).toBe('-5 000 Kč')
  })
})

describe('getPeriodLabel', () => {
  it('returns label for current-year', () => {
    expect(getPeriodLabel('current-year')).toBe('Aktuální rok')
  })

  it('returns label for current-and-previous-year', () => {
    expect(getPeriodLabel('current-and-previous-year')).toBe('Aktuální + předchozí rok')
  })

  it('returns label for last-6-months', () => {
    expect(getPeriodLabel('last-6-months')).toBe('Posledních 6 měsíců')
  })

  it('returns label for last-13-months', () => {
    expect(getPeriodLabel('last-13-months')).toBe('Posledních 13 měsíců')
  })

  it('returns label for last-26-months', () => {
    expect(getPeriodLabel('last-26-months')).toBe('Posledních 26 měsíců')
  })
})

describe('MONTH_SLOT_WIDTH', () => {
  it('is wide enough to display a month label (minimum 40px)', () => {
    expect(MONTH_SLOT_WIDTH).toBeGreaterThanOrEqual(40)
  })
})
