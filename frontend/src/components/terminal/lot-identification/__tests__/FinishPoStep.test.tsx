import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import FinishPoStep from '../FinishPoStep';

const mockMutate = jest.fn();

jest.mock('../../../../api/hooks/usePurchaseOrders', () => ({
  useUpdatePurchaseOrderStatusMutation: () => ({ mutate: mockMutate, isPending: false }),
}));

beforeEach(() => mockMutate.mockReset());

const wrap = (entry: string) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/terminal/lot-identification/po/:id/finish" element={<FinishPoStep />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

test('clicking confirm calls mutate with Received status', async () => {
  wrap('/terminal/lot-identification/po/1/finish');
  fireEvent.click(screen.getByRole('button', { name: /označit jako přijatou/i }));
  await waitFor(() =>
    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: 1, request: expect.objectContaining({ status: 'Received' }) }),
      expect.anything()
    )
  );
});

test('renders skip button', () => {
  wrap('/terminal/lot-identification/po/2/finish');
  expect(screen.getByRole('button', { name: /Ponechat ve stavu/i })).toBeInTheDocument();
});
