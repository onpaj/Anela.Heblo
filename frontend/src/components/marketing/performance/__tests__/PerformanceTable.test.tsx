import React from 'react'
import { render, screen, within } from '@testing-library/react'
import { PerformanceTable } from '../PerformanceTable'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

const channels = [{ code: 'meta', label: 'FB/IG' }, { code: 'google', label: 'Google' }] as ChannelInfoDto[]

const month = (overrides: Partial<MonthlyMarketingPerformanceDto>): MonthlyMarketingPerformanceDto =>
  ({
    year: 2026, month: 7, monthYearDisplay: '07/2026', hasData: true, isLocked: false, isPartial: false,
    orders: 1527, revenueWithVat: 1688112, revenueWithoutVat: 1395134, totalCost: 438069, pno: 31.4, roas: 318.5,
    profit: 957065, avgOrderValue: 913.6, costPerOrder: 286.9, yoyCostPercent: 84.6, yoyRevenuePercent: 77.7, yoyOrdersPercent: 83.2,
    skippedEurInvoiceCount: 0, revenueComputedAt: '2026-09-18T03:00:00Z', costsComputedAt: '2026-09-18T03:00:00Z', lastError: null,
    channelCosts: [
      { channelCode: 'meta', label: 'FB/IG', costWithoutVat: 327126, invoiceCount: 2 },
      { channelCode: 'google', label: 'Google', costWithoutVat: 99909, invoiceCount: 1 },
    ],
    ...overrides,
  }) as unknown as MonthlyMarketingPerformanceDto

const norm = (s: string | null) => (s ?? '').replace(/ /g, ' ')

describe('PerformanceTable', () => {
  it('renders one channel column per configured channel, in spreadsheet order, newest month first', () => {
    render(<PerformanceTable channels={channels} months={[month({ month: 6, monthYearDisplay: '06/2026' }), month({})]} />)

    const headers = screen.getAllByRole('columnheader').map((h) => norm(h.textContent))
    expect(headers).toEqual([
      'Měsíc', 'FB/IG', 'Google', 'Náklady celkem', 'Tržby s DPH', 'Tržby bez DPH', 'PNO', 'ROAS',
      'Rozdíl tržby − náklady', 'Objednávky', 'Prům. objednávka', 'Cena za nákup', 'Náklady r/r', 'Tržby r/r', 'Objednávky r/r',
    ])
    const rows = screen.getAllByRole('row').slice(1)
    expect(norm(within(rows[0]).getAllByRole('cell')[0].textContent)).toBe('07/2026')
    expect(norm(within(rows[1]).getAllByRole('cell')[0].textContent)).toBe('06/2026')
  })

  it('formats values in Czech and shows dashes for null ratios', () => {
    render(<PerformanceTable channels={channels} months={[month({ pno: null, roas: null })]} />)
    // eslint-disable-next-line testing-library/no-node-access
    const cells = screen.getAllByRole('row')[1].querySelectorAll('td')
    expect(norm(cells[1].textContent)).toBe('327 126 Kč')
    expect(norm(cells[3].textContent)).toBe('438 069 Kč')
    expect(norm(cells[6].textContent)).toBe('—')
    expect(norm(cells[9].textContent)).toBe('1 527')
    expect(norm(cells[12].textContent)).toBe('−15,4 %')
  })

  it('flags partial, locked and errored months', () => {
    render(
      <PerformanceTable
        channels={channels}
        months={[
          month({ month: 9, monthYearDisplay: '09/2026', isPartial: true, lastError: 'Costs: Flexi 503', costsComputedAt: null }),
          month({ month: 8, monthYearDisplay: '08/2026', isLocked: true }),
        ]}
      />,
    )
    expect(screen.getByText(/09\/2026/)).toHaveTextContent('(probíhá)')
    expect(screen.getByTitle('Costs: Flexi 503')).toBeInTheDocument()
    expect(screen.getByTitle('Měsíc je uzamčen — mění ho jen ruční přepočet')).toBeInTheDocument()
  })

  it('gives a de-configured channel its own column so the row still adds up to the total', () => {
    // Arrange - sklik left the config; its stored cost is still part of totalCost.
    const withOrphan = month({
      totalCost: 438069 + 11034,
      channelCosts: [
        { channelCode: 'meta', label: 'FB/IG', costWithoutVat: 327126, invoiceCount: 2 },
        { channelCode: 'google', label: 'Google', costWithoutVat: 99909, invoiceCount: 1 },
        { channelCode: 'sklik', label: 'S-Klik', costWithoutVat: 11034, invoiceCount: 1 },
      ],
    } as Partial<MonthlyMarketingPerformanceDto>)

    // Act
    render(<PerformanceTable channels={channels} months={[withOrphan]} />)

    // Assert - the orphan follows the configured columns rather than vanishing from the row.
    const headers = screen.getAllByRole('columnheader').map((h) => norm(h.textContent))
    expect(headers.slice(0, 5)).toEqual(['M\u011bs\u00edc', 'FB/IG', 'Google', 'S-Klik', 'N\u00e1klady celkem'])
  })

  it('renders an empty state when no month has data', () => {
    render(<PerformanceTable channels={channels} months={[month({ hasData: false })]} />)
    expect(screen.getByText('Zatím nejsou k dispozici žádná data. Spusťte přepočet nebo počkejte na noční úlohu.')).toBeInTheDocument()
  })
})
