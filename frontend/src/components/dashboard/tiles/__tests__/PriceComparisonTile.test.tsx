import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { PriceComparisonTile } from '../PriceComparisonTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

jest.mock('../../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

const drillDown = { routeKey: 'productPricing', enabled: true };

const renderTile = (data: any) =>
  render(
    <BrowserRouter>
      <PriceComparisonTile data={data} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('PriceComparisonTile', () => {
  it('navigates to /products/pricing on click in the no_data state', () => {
    renderTile({ status: 'no_data', drillDown });

    fireEvent.click(screen.getByText('Žádná data'));
    expect(mockNavigate).toHaveBeenCalledWith('/products/pricing');
  });

  it('navigates on click in the success state', () => {
    renderTile({
      status: 'success',
      data: { totalMismatches: 0, totalChecked: 100, completedAt: '2026-09-10T06:00:00Z' },
      drillDown,
    });

    fireEvent.click(screen.getByText('vše OK', { exact: true }));
    expect(mockNavigate).toHaveBeenCalledWith('/products/pricing');
  });

  it('shows the mismatch count and products examined in the warning state', () => {
    renderTile({
      status: 'warning',
      data: { totalMismatches: 7, totalChecked: 400, completedAt: '2026-09-10T06:00:00Z' },
      drillDown,
    });

    expect(screen.getByText('7')).toBeInTheDocument();
    expect(screen.getByText('neshod')).toBeInTheDocument();
    expect(screen.getByText('ze 400 produktů')).toBeInTheDocument();
  });

  it('does not navigate when the route key is unknown', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    renderTile({
      status: 'success',
      data: { totalMismatches: 0, totalChecked: 100, completedAt: '2026-09-10T06:00:00Z' },
      drillDown: { routeKey: 'somethingNew', enabled: true },
    });

    fireEvent.click(screen.getByText('vše OK', { exact: true }));
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
    warnSpy.mockRestore();
  });

  it('renders an error state without a total-checked figure', () => {
    renderTile({ status: 'error', drillDown });

    expect(screen.getByText('Poslední kontrola cen selhala')).toBeInTheDocument();
  });
});
