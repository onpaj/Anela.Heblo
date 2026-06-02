import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DqtYesterdayStatusTile } from '../DqtYesterdayStatusTile';

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
      <DqtYesterdayStatusTile data={data} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('DqtYesterdayStatusTile', () => {
  it('renders no_data state', () => {
    renderTile({ status: 'no_data', data: null, drillDown });

    expect(screen.getByText('Žádná data')).toBeInTheDocument();
    expect(screen.getByText('Včerejší test neproběhl')).toBeInTheDocument();
  });

  it('renders error state and does not navigate on click', () => {
    renderTile({ status: 'error', data: null, drillDown });

    expect(screen.getByText('Chyba při načítání dat')).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('renders warning state with Running runStatus', () => {
    renderTile({
      status: 'warning',
      data: {
        runId: 'r1',
        runStatus: 'Running',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 0,
        totalMismatches: 0,
      },
      drillDown,
    });

    expect(screen.getByText('probíhá')).toBeInTheDocument();
    expect(screen.getByText('05.05.2026')).toBeInTheDocument();
  });

  it('renders warning state with completed run and mismatches', () => {
    renderTile({
      status: 'warning',
      data: {
        runId: 'r2',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 123,
        totalMismatches: 4,
      },
      drillDown,
    });

    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('neshod')).toBeInTheDocument();
    expect(screen.getByText('z 123 faktur')).toBeInTheDocument();
    expect(screen.getByText('05.05.2026')).toBeInTheDocument();
  });

  it('renders success state', () => {
    renderTile({
      status: 'success',
      data: {
        runId: 'r3',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 200,
        totalMismatches: 0,
      },
      drillDown,
    });

    expect(screen.getByText('0')).toBeInTheDocument();
    expect(screen.getByText('vše OK')).toBeInTheDocument();
    expect(screen.getByText('z 200 faktur')).toBeInTheDocument();
    expect(screen.getByText('05.05.2026')).toBeInTheDocument();
  });

  it('falls back to "včera" label when dateTo is missing', () => {
    renderTile({
      status: 'warning',
      data: {
        runId: 'r4',
        runStatus: 'Running',
        totalChecked: 0,
        totalMismatches: 0,
      },
      drillDown,
    });

    expect(screen.getByText('včera')).toBeInTheDocument();
  });

  it('navigates to /automation/data-quality when clicked (success state)', () => {
    renderTile({
      status: 'success',
      data: {
        runId: 'r5',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 50,
        totalMismatches: 0,
      },
      drillDown,
    });

    const tile = screen.getByTestId('dqt-yesterday-tile');
    fireEvent.click(tile);
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });

  it('does not navigate when the route key is unknown', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    renderTile({
      status: 'success',
      data: {
        runId: 'r6',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 50,
        totalMismatches: 0,
      },
      drillDown: { routeKey: 'somethingNew', enabled: true },
    });

    fireEvent.click(screen.getByTestId('dqt-yesterday-tile'));
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
    warnSpy.mockRestore();
  });
});
