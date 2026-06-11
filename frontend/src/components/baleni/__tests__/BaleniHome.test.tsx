import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import BaleniHome from '../BaleniHome';

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
  it('renders heading', () => {
    renderHome();
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

  it('renders exactly 3 tiles', () => {
    renderHome();
    expect(screen.getAllByTestId(/^baleni-tile-/)).toHaveLength(3);
  });
});
