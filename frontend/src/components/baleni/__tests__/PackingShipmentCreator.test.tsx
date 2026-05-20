import React from 'react'
import { render, screen, fireEvent } from '@testing-library/react'
import PackingShipmentCreator from '../PackingShipmentCreator'
import { useCreateShipment } from '../../../api/hooks/useCreateShipment'
import { useShipmentLabels } from '../../../api/hooks/useShipmentLabels'

jest.mock('../../../api/hooks/useCreateShipment', () => ({
  useCreateShipment: jest.fn(),
}))
jest.mock('../../../api/hooks/useShipmentLabels', () => ({
  useShipmentLabels: jest.fn(),
}))
jest.mock('../PackingLabelPrinter', () => ({
  __esModule: true,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  default: (props: any) => (
    <div data-testid="packing-label-printer" data-order-code={props.orderCode} />
  ),
}))

const mockUseCreateShipment = useCreateShipment as jest.Mock
const mockUseShipmentLabels = useShipmentLabels as jest.Mock

const idleMutation = {
  mutate: jest.fn(),
  isPending: false,
  isSuccess: false,
  isError: false,
  data: undefined,
  error: null,
  reset: jest.fn(),
}

const idleLabels = { data: undefined, isLoading: false, isError: false, refetch: jest.fn() }

beforeEach(() => {
  jest.clearAllMocks()
  mockUseCreateShipment.mockReturnValue({ ...idleMutation })
  mockUseShipmentLabels.mockReturnValue({ ...idleLabels })
})

describe('PackingShipmentCreator', () => {
  it('shows Vytvořit zásilku button in idle state', () => {
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByRole('button', { name: /Vytvořit zásilku/i })).toBeInTheDocument()
  })

  it('clicking Vytvořit zásilku calls mutate with forceCreate=false', () => {
    const mutate = jest.fn()
    mockUseCreateShipment.mockReturnValue({ ...idleMutation, mutate })
    render(<PackingShipmentCreator orderCode="0001234" />)

    fireEvent.click(screen.getByRole('button', { name: /Vytvořit zásilku/i }))

    expect(mutate).toHaveBeenCalledWith({ orderCode: '0001234', forceCreate: false })
  })

  it('shows spinner while creating', () => {
    mockUseCreateShipment.mockReturnValue({ ...idleMutation, isPending: true })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByTestId('shipment-creating-spinner')).toBeInTheDocument()
  })

  it('shows PackingLabelPrinter when label is ready', () => {
    mockUseCreateShipment.mockReturnValue({
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
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: { labelReady: false, labels: [], existingShipmentFound: false },
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByRole('button', { name: /Zkusit znovu/i })).toBeInTheDocument()
  })

  it('shows PackingLabelPrinter when labelsQuery returns data after retry', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: { labelReady: false, labels: [], existingShipmentFound: false },
    })
    mockUseShipmentLabels.mockReturnValue({
      ...idleLabels,
      data: [{ packageName: 'P1', labelUrl: 'https://x.com/label.pdf' }],
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByTestId('packing-label-printer')).toBeInTheDocument()
  })

  it('Zkusit znovu calls refetch on useShipmentLabels', () => {
    const refetch = jest.fn()
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: { labelReady: false, labels: [], existingShipmentFound: false },
    })
    mockUseShipmentLabels.mockReturnValue({ ...idleLabels, refetch })
    render(<PackingShipmentCreator orderCode="0001234" />)

    fireEvent.click(screen.getByRole('button', { name: /Zkusit znovu/i }))
    expect(refetch).toHaveBeenCalled()
  })

  it('shows existing shipment warning and reuse / create-new buttons', () => {
    mockUseCreateShipment.mockReturnValue({
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
    mockUseCreateShipment.mockReturnValue({
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

  it('clicking Vytvořit novou calls mutate with forceCreate=true', () => {
    const mutate = jest.fn()
    mockUseCreateShipment.mockReturnValue({
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
    expect(mutate).toHaveBeenCalledWith({ orderCode: '0001234', forceCreate: true })
  })

  it('shows error banner on mutation error', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isError: true,
      error: new Error('Shoptet nemohl vytvořit zásilku — zkuste znovu'),
    })
    render(<PackingShipmentCreator orderCode="0001234" />)
    expect(screen.getByTestId('shipment-error-banner')).toBeInTheDocument()
    expect(screen.getByText(/Shoptet nemohl vytvořit zásilku/i)).toBeInTheDocument()
  })
})
