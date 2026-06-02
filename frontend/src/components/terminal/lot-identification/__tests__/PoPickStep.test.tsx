import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import PoPickStep from '../PoPickStep';

const mockUsePurchaseOrdersQuery = jest.fn();

jest.mock('../../../../api/hooks/usePurchaseOrders', () => ({
  usePurchaseOrdersQuery: (...args: unknown[]) => mockUsePurchaseOrdersQuery(...args),
}));

const TWO_POS = {
  orders: [
    { id: 1, orderNumber: 'PO-001', supplierName: 'Acme', status: 'InTransit' },
    { id: 2, orderNumber: 'PO-002', supplierName: 'Beta', status: 'InTransit' },
  ],
};

const wrap = (children: React.ReactNode) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
};

beforeEach(() => {
  mockUsePurchaseOrdersQuery.mockReturnValue({ data: TWO_POS, isLoading: false });
});

test('renders one row per InTransit PO', () => {
  wrap(<PoPickStep />);
  expect(screen.getByText('PO-001')).toBeInTheDocument();
  expect(screen.getByText('PO-002')).toBeInTheDocument();
});

test('PO row links to /terminal/lot-identification/po/{id}', () => {
  wrap(<PoPickStep />);
  expect(screen.getByRole('link', { name: /PO-001/ })).toHaveAttribute(
    'href',
    '/terminal/lot-identification/po/1'
  );
});

test('shows empty message when no POs returned', () => {
  mockUsePurchaseOrdersQuery.mockReturnValue({ data: { orders: [] }, isLoading: false });
  wrap(<PoPickStep />);
  expect(screen.getByText(/Žádné objednávky/)).toBeInTheDocument();
});
