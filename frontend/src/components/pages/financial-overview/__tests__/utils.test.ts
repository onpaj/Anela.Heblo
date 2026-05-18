import { formatCurrency, getPeriodLabel, MONTH_SLOT_WIDTH } from '../utils'

describe('formatCurrency', () => {
  it('returns a string containing Kč', () => {
    expect(formatCurrency(1000)).toMatch(/Kč/)
  })

  it('formats zero', () => {
    expect(formatCurrency(0)).toMatch(/Kč/)
  })

  it('formats negative amount', () => {
    expect(formatCurrency(-5000)).toMatch(/Kč/)
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
  it('equals 48', () => {
    expect(MONTH_SLOT_WIDTH).toBe(48)
  })
})
