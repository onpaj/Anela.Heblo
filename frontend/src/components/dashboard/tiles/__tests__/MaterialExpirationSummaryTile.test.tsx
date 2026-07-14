import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { MaterialExpirationSummaryTile } from '../MaterialExpirationSummaryTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

const renderWithRouter = (component: React.ReactElement) => {
  return render(<BrowserRouter>{component}</BrowserRouter>);
};

describe('MaterialExpirationSummaryTile', () => {
  beforeEach(() => {
    mockNavigate.mockClear();
  });

  it('renders the four bucket labels', () => {
    const data = { status: 'success', data: { expired: 0, within30: 0, within90: 0, ok: 0 } };

    renderWithRouter(<MaterialExpirationSummaryTile data={data} />);

    expect(screen.getByText('Po expiraci')).toBeInTheDocument();
    expect(screen.getByText('≤ 30 dní')).toBeInTheDocument();
    expect(screen.getByText('31–90 dní')).toBeInTheDocument();
    expect(screen.getByText('V pořádku')).toBeInTheDocument();
  });

  it('renders the counts for each bucket', () => {
    const data = { status: 'success', data: { expired: 4, within30: 2, within90: 7, ok: 9 } };

    renderWithRouter(<MaterialExpirationSummaryTile data={data} />);

    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('7')).toBeInTheDocument();
    expect(screen.getByText('9')).toBeInTheDocument();
  });

  it('defaults missing bucket values to zero', () => {
    const data = { status: 'success', data: {} };

    renderWithRouter(<MaterialExpirationSummaryTile data={data} />);

    expect(screen.getAllByText('0')).toHaveLength(4);
  });

  it('renders error state when status is error', () => {
    const data = { status: 'error', error: 'Failed to load data' };

    renderWithRouter(<MaterialExpirationSummaryTile data={data} />);

    expect(screen.getByText('Failed to load data')).toBeInTheDocument();
    expect(screen.getByText('⚠️')).toBeInTheDocument();
  });

  it('navigates to the filtered URL on click when drillDown is present', () => {
    const data = {
      status: 'success',
      data: { expired: 1, within30: 0, within90: 0 },
      drillDown: { enabled: true, filters: { type: 'Material' } },
    };

    renderWithRouter(<MaterialExpirationSummaryTile data={data} targetUrl="/manufacturing/inventory" />);

    fireEvent.click(screen.getByText('Po expiraci'));

    expect(mockNavigate).toHaveBeenCalledTimes(1);
    expect(mockNavigate).toHaveBeenCalledWith(expect.stringContaining('/manufacturing/inventory'));
  });

  it('does not navigate when drillDown is absent', () => {
    const data = { status: 'success', data: { expired: 1, within30: 0, within90: 0 } };

    renderWithRouter(<MaterialExpirationSummaryTile data={data} targetUrl="/manufacturing/inventory" />);

    fireEvent.click(screen.getByText('Po expiraci'));

    expect(mockNavigate).not.toHaveBeenCalled();
  });
});
