export type PeriodType =
  | 'current-year'
  | 'current-and-previous-year'
  | 'last-6-months'
  | 'last-13-months'
  | 'last-26-months'

export const MONTH_SLOT_WIDTH = 48

export const formatCurrency = (amount: number): string =>
  new Intl.NumberFormat('cs-CZ', {
    style: 'currency',
    currency: 'CZK',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(amount)

export const getPeriodLabel = (period: PeriodType): string => {
  switch (period) {
    case 'current-year':
      return 'Aktuální rok'
    case 'current-and-previous-year':
      return 'Aktuální + předchozí rok'
    case 'last-6-months':
      return 'Posledních 6 měsíců'
    case 'last-13-months':
      return 'Posledních 13 měsíců'
    case 'last-26-months':
      return 'Posledních 26 měsíců'
    default: {
      const _exhaustive: never = period
      throw new Error(`Unhandled period: ${_exhaustive}`)
    }
  }
}
