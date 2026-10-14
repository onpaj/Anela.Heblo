import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { JournalEntryDto } from '../../../../api/generated/api-client'
import { FinancialJournalPanel } from '../FinancialJournalPanel'

// Plain component (not jest.fn) — CRA's resetMocks would strip a jest.fn implementation.
jest.mock('../../../JournalEntryModal', () => ({
  __esModule: true,
  default: ({ isOpen, isEdit, entry }: { isOpen: boolean; isEdit?: boolean; entry?: { title?: string } }) =>
    isOpen ? (
      <div data-testid="journal-modal">
        {isEdit ? 'edit' : 'create'}:{entry?.title ?? ''}
      </div>
    ) : null,
}))

const STOCKTAKE = new JournalEntryDto({
  id: 1,
  title: 'Inventura etiket',
  content: 'Odpis etiket I-00075/2026',
  entryDate: new Date(2026, 7, 3),
  tags: [],
})

describe('FinancialJournalPanel', () => {
  it('lists entries with their title and date', () => {
    render(<FinancialJournalPanel entries={[STOCKTAKE]} isLoading={false} isError={false} />)

    expect(screen.getByText('Záznamy deníku (1)')).toBeInTheDocument()
    expect(screen.getByText('Inventura etiket')).toBeInTheDocument()
    expect(screen.getByText('03.08.2026')).toBeInTheDocument()
  })

  it('opens the modal in edit mode for a clicked entry', () => {
    render(<FinancialJournalPanel entries={[STOCKTAKE]} isLoading={false} isError={false} />)

    fireEvent.click(screen.getByText('Inventura etiket'))

    expect(screen.getByTestId('journal-modal')).toHaveTextContent('edit:Inventura etiket')
  })

  it('opens the modal in create mode with no entry', () => {
    render(<FinancialJournalPanel entries={[STOCKTAKE]} isLoading={false} isError={false} />)

    fireEvent.click(screen.getByRole('button', { name: /Přidat záznam/ }))

    expect(screen.getByTestId('journal-modal')).toHaveTextContent('create:')
  })

  it('shows an empty state when the period has no entries', () => {
    render(<FinancialJournalPanel entries={[]} isLoading={false} isError={false} />)

    expect(screen.getByText('Pro zobrazené období nejsou žádné firemní záznamy deníku.')).toBeInTheDocument()
  })

  it('shows loading and error states', () => {
    const { rerender } = render(<FinancialJournalPanel entries={[]} isLoading isError={false} />)
    expect(screen.getByText('Načítání záznamů deníku...')).toBeInTheDocument()

    rerender(<FinancialJournalPanel entries={[]} isLoading={false} isError />)
    expect(screen.getByText('Záznamy deníku se nepodařilo načíst.')).toBeInTheDocument()
  })
})
