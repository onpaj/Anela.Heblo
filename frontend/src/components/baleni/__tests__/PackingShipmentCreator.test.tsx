import React from 'react'
import { render, screen, fireEvent } from '@testing-library/react'
import PackingShipmentCreator from '../PackingShipmentCreator'
import { usePrepareOrderLabel } from '../../../api/hooks/usePrepareOrderLabel'

jest.mock('../../../api/hooks/usePrepareOrderLabel', () => ({
  usePrepareOrderLabel: jest.fn(),
}))
jest.mock('../PackingLabelPrinter', () => ({
  __esModule: true,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  default: (props: any) => (
    <div data-testid="packing-label-printer" data-order-code={props.orderCode} />
  ),
}))

const mockUsePrepareOrderLabel = usePrepareOrderLabel as jest.Mock

const idleMutation = {
  mutate: jest.fn(),
  isPending: false,
  isSuccess: false,
  isError: false,
  data: undefined,
  error: null,
  reset: jest.fn(),
}

beforeEach(() => {
  jest.clearAllMocks()
  mockUsePrepareOrderLabel.mockReturnValue({ ...idleMutation })
})

describe('PackingShipmentCreator', () => {
  it('shows Vytvořit zásilku button in idle state', () => {
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByRole('button', { name: /Vytvořit zásilku/i })).toBeInTheDocument()
  })

  it('clicking Vytvořit zásilku calls mutate with forceRecreate=false', () => {
    const mutate = jest.fn()
    mockUsePrepareOrderLabel.mockReturnValue({ ...idleMutation, mutate })
    render(<PackingShipmentCreator orderCode="0001234" />)

    fireEvent.click(screen.getByRole('button', { name: /Vytvořit zásilku/i }))

    expect(mutate).toHaveBeenCalledWith({ orderCode: '0001234', forceRecreate: false })
  })

  it('shows spinner while creating', () => {
    mockUsePrepareOrderLabel.mockReturnValue({ ...idleMutation, isPending: true })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByTestId('shipment-creating-spinner')).toBeInTheDocument()
  })

  it('shows PackingLabelPrinter when label is ready', () => {
    mockUsePrepareOrderLabel.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ packageName: 'P1', labelUrl: 'https://x.com' }],
        existingShipmentFound: false,
      },
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByTestId('packing-label-printer')).toBeInTheDocument()
  })

  it('shows Zkusit znovu button when labelReady is false', () => {
    mockUsePrepareOrderLabel.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: { labelReady: false, labels: [], existingShipmentFound: false },
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByRole('button', { name: /Zkusit znovu/i })).toBeInTheDocument()
  })

  it('shows existing shipment warning and reuse / create-new buttons', () => {
    mockUsePrepareOrderLabel.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      },
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByText(/Zásilka již existuje/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Použít existující/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Vytvořit novou/i })).toBeInTheDocument()
  })

  it('clicking Použít existující renders label printer', () => {
    mockUsePrepareOrderLabel.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ shipmentGuid: 'g1', packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      },
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    fireEvent.click(screen.getByRole('button', { name: /Použít existující/i }))
    expect(screen.getByTestId('packing-label-printer')).toBeInTheDocument()
  })

  it('clicking Vytvořit novou calls mutate with forceRecreate=true', () => {
    const mutate = jest.fn()
    mockUsePrepareOrderLabel.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      },
      mutate,
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    fireEvent.click(screen.getByRole('button', { name: /Vytvořit novou/i }))
    expect(mutate).toHaveBeenCalledWith({ orderCode: '0001234', forceRecreate: true })
  })

  it('shows error banner on mutation error', () => {
    mockUsePrepareOrderLabel.mockReturnValue({
      ...idleMutation,
      isError: true,
      error: new Error('Shoptet nemohl vytvořit zásilku — zkuste znovu'),
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByTestId('shipment-error-banner')).toBeInTheDocument()
    expect(screen.getByText(/Shoptet nemohl vytvořit zásilku/i)).toBeInTheDocument()
  })
})
