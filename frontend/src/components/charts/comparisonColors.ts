export const YEAR_ALPHAS = [1, 0.55, 0.3] as const

export type Rgb = [number, number, number]

/** Colour for a series at a given year slot (0 = anchor/current year). Hue encodes the metric, alpha the year. */
export const withYearAlpha = ([r, g, b]: Rgb, yearIndex: number): string => {
  const alpha = YEAR_ALPHAS[Math.min(yearIndex, YEAR_ALPHAS.length - 1)]
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}
