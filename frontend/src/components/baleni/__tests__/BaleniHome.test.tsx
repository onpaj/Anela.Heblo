import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import BaleniHome from '../BaleniHome';
import { GetPackingDashboardResponse } from '../../../api/hooks/usePackingDashboard';

const mockUsePackingDashboard = jest.fn();
jest.mock('../../../api/hooks/usePackingDashboard', () => ({
  ...jest.requireActual('../../../api/hooks/usePackingDashboard'),
  usePackingDashboard: () => mockUsePackingDashboard(),
}));

const dashboard = (overrides: Partial<GetPackingDashboardResponse>): GetPackingDashboardResponse => ({
  ordersBeingPackedCount: 3,
  ordersBeingProcessedCount: 7,
  ordersBeingPackedCountLastSync: null,
  totalOrdersPackedToday: 42,
  canViewPackerBreakdown: true,
  packedByPacker: [{ packerId: 'u1', packerName: 'Jana', orderCount: 20 }],
  ...overrides,
});

const renderHome = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <BaleniHome />
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

describe('BaleniHome', () => {
  beforeEach(() => {
    mockUsePackingDashboard.mockReturnValue({ data: undefined, isLoading: false });
  });

  it('shows the per-packer breakdown to users allowed to see it', () => {
    mockUsePackingDashboard.mockReturnValue({ data: dashboard({}), isLoading: false });
    renderHome();
    expect(screen.getByText('Zabaleno dnes podle baličů')).toBeInTheDocument();
    expect(screen.getByText('Jana')).toBeInTheDocument();
  });

  it('shows only the daily total to users without the packer statistics permission', () => {
    mockUsePackingDashboard.mockReturnValue({
      data: dashboard({ canViewPackerBreakdown: false, packedByPacker: [] }),
      isLoading: false,
    });
    renderHome();
    expect(screen.getByText('42')).toBeInTheDocument();
    expect(screen.queryByText('Zabaleno dnes podle baličů')).not.toBeInTheDocument();
    expect(screen.queryByText(/nikdo nezabalil/)).not.toBeInTheDocument();
  });

  it('renders section headings', () => {
    renderHome();
    expect(screen.getByText('Přehled')).toBeInTheDocument();
    expect(screen.getByText('Navigace')).toBeInTheDocument();
  });

  it('renders Balení tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-baleni');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/baleni/baleni');
  });

  it('renders Zásilky tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-zasilky');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/baleni/zasilky');
  });

  it('renders Statistiky tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-statistiky');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/baleni/statistiky');
  });

  it('renders Nastavení štítků tile with correct href', () => {
    renderHome();
    const tile = screen.getByTestId('baleni-tile-nastaveni-stitku');
    expect(tile).toHaveAttribute('href', '/baleni/nastaveni-stitku');
  });

  it('renders exactly 4 tiles', () => {
    renderHome();
    expect(screen.getAllByTestId(/^baleni-tile-/)).toHaveLength(4);
  });
});
