import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import PurchaseOrderList from '../PurchaseOrderList';

const mockTrackEvent = jest.fn();
jest.mock('../../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const mockRefetch = jest.fn().mockResolvedValue(undefined);
jest.mock('../../../api/hooks/usePurchaseOrders', () => ({
  usePurchaseOrdersQuery: () => ({
    data: { orders: [], totalCount: 0, totalPages: 0 },
    isLoading: false,
    error: null,
    refetch: mockRefetch,
  }),
}));

jest.mock('../PurchaseOrderDetail', () => ({
  __esModule: true,
  default: () => <div data-testid="order-detail" />,
}));

jest.mock('../PurchaseOrderForm', () => ({
  __esModule: true,
  default: ({ onSuccess }: { onSuccess: (id: number) => void }) => (
    <button
      data-testid="mock-form-submit"
      onClick={() => onSuccess(42)}
    >
      Submit
    </button>
  ),
}));

function renderList() {
  return render(
    <MemoryRouter>
      <PurchaseOrderList />
    </MemoryRouter>
  );
}

describe('PurchaseOrderList telemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('tracks PurchaseOrderSubmitted when handleCreateSuccess is called', async () => {
    renderList();

    const createButton = screen.getByRole('button', { name: /nová objednávka/i });
    await userEvent.click(createButton);

    const submitBtn = screen.getByTestId('mock-form-submit');
    await userEvent.click(submitBtn);

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith('PurchaseOrderSubmitted', { orderId: '42' });
    });
  });
});
