import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import PoLinePickStep from '../PoLinePickStep';

jest.mock('../../../../api/hooks/usePurchaseOrders', () => ({
  usePurchaseOrderDetailQuery: (_id: number) => ({
    data: {
      id: 1,
      orderNumber: 'PO-001',
      lines: [
        { id: 10, materialId: 'MAT001', materialName: 'Olive Oil', quantity: 100 },
        { id: 11, materialId: 'MAT002', materialName: 'Shea Butter', quantity: 50 },
      ],
    },
    isLoading: false,
  }),
}));

const wrap = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/terminal/lot-identification/po/1']}>
        <Routes>
          <Route
            path="/terminal/lot-identification/po/:id"
            element={<PoLinePickStep />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

test('renders one row per PO line', () => {
  wrap();
  expect(screen.getByText('Olive Oil')).toBeInTheDocument();
  expect(screen.getByText('Shea Butter')).toBeInTheDocument();
});

test('line row links to /terminal/lot-identification/po/{poId}/line/{lineId}/lot', () => {
  wrap();
  expect(screen.getByRole('link', { name: /Olive Oil/ })).toHaveAttribute(
    'href',
    '/terminal/lot-identification/po/1/line/10/lot'
  );
});

test('renders order number in heading', () => {
  wrap();
  expect(screen.getByText(/PO-001/)).toBeInTheDocument();
});
