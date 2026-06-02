import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DataQualityTile } from '../DataQualityTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

jest.mock('../../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

const drillDown = { routeKey: 'dataQuality', enabled: true };

const renderTile = (data: any) =>
  render(
    <BrowserRouter>
      <DataQualityTile data={data} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('DataQualityTile', () => {
  it('navigates to /automation/data-quality on click in the no_data state', () => {
    renderTile({ status: 'no_data', drillDown });

    fireEvent.click(screen.getByText('Žádná data'));
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });

  it('navigates on click in the success state', () => {
    renderTile({
      status: 'success',
      data: { mismatchCount: 0, totalChecked: 100, dateFrom: '2026-05-05', dateTo: '2026-05-05' },
      drillDown,
    });

    fireEvent.click(screen.getByText('vše OK'));
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });

  it('does not navigate when the route key is unknown', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    renderTile({
      status: 'success',
      data: { mismatchCount: 0, totalChecked: 100, dateFrom: '2026-05-05', dateTo: '2026-05-05' },
      drillDown: { routeKey: 'somethingNew', enabled: true },
    });

    fireEvent.click(screen.getByText('vše OK'));
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
    warnSpy.mockRestore();
  });
});
