import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { RecomputeDialog } from '../RecomputeDialog'

const mockMutate = jest.fn()
let mockMutationState: Record<string, unknown> = {}
jest.mock('../../../../api/hooks/useMarketingPerformance', () => ({
  useRecomputeMarketingPerformanceMutation: () => ({ mutate: mockMutate, isPending: false, reset: jest.fn(), ...mockMutationState }),
}))

const renderDialog = (isOpen: boolean, onClose: () => void) =>
  render(
    <MemoryRouter>
      <RecomputeDialog isOpen={isOpen} onClose={onClose} />
    </MemoryRouter>,
  )

describe('RecomputeDialog', () => {
  beforeEach(() => { jest.clearAllMocks(); mockMutationState = {} })

  it('submits the typed range', () => {
    renderDialog(true, jest.fn())
    fireEvent.change(screen.getByLabelText('Od (RRRR-MM)'), { target: { value: '2023-01' } })
    fireEvent.change(screen.getByLabelText('Do (RRRR-MM)'), { target: { value: '2024-12' } })
    fireEvent.click(screen.getByRole('button', { name: 'Spustit přepočet' }))
    expect(mockMutate).toHaveBeenCalledWith({ from: '2023-01', to: '2024-12' }, expect.anything())
  })

  it('blocks submit on a malformed month', () => {
    renderDialog(true, jest.fn())
    fireEvent.change(screen.getByLabelText('Od (RRRR-MM)'), { target: { value: '2023-1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Spustit přepočet' }))
    expect(mockMutate).not.toHaveBeenCalled()
    expect(screen.getByText('Zadejte měsíce ve formátu RRRR-MM.')).toBeInTheDocument()
  })

  it('shows job id and link after success', () => {
    mockMutationState = { isSuccess: true, data: { success: true, jobId: 'hf-9', monthCount: 24 } }
    renderDialog(true, jest.fn())
    expect(screen.getByText(/hf-9/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Naplánované úlohy' })).toHaveAttribute('href', '/recurring-jobs')
  })

  it('renders nothing when closed', () => {
    const { container } = renderDialog(false, jest.fn())
    expect(container).toBeEmptyDOMElement()
  })
})
