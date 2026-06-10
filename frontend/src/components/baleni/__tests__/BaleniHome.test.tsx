import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import BaleniHome from '../BaleniHome';

const renderHome = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BaleniHome />
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

describe('BaleniHome', () => {
  it('renders heading', () => {
    renderHome();
    // 'Vyberte operaci' on feature branch; 'Navigace' after merging main's dashboard rework.
    const heading =
      screen.queryByText('Vyberte operaci') ?? screen.queryByText('Navigace');
    expect(heading).toBeInTheDocument();
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
