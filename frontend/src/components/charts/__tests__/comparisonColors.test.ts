import { withYearAlpha, YEAR_ALPHAS } from '../comparisonColors'

describe('withYearAlpha', () => {
  it('uses full alpha for the anchor year and fades older years', () => {
    expect(withYearAlpha([34, 197, 94], 0)).toBe('rgba(34, 197, 94, 1)')
    expect(withYearAlpha([34, 197, 94], 1)).toBe(`rgba(34, 197, 94, ${YEAR_ALPHAS[1]})`)
  })
  it('clamps year index to the last alpha', () => {
    expect(withYearAlpha([1, 2, 3], 9)).toBe(`rgba(1, 2, 3, ${YEAR_ALPHAS[2]})`)
  })
})
