import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MarketingAdsProposalsPage from '../MarketingAdsProposalsPage';
import { buildProposalSummary } from '../../__fixtures__/adsFixtures';

const mockProposalsQuery = jest.fn();
jest.mock('../../../../../api/hooks/useMarketingAdsProposals', () => ({
  useAdProposalsQuery: (...args: unknown[]) => mockProposalsQuery(...args),
}));
jest.mock('../ProposalDetail', () => ({
  __esModule: true,
  default: ({ proposalId }: { proposalId: string }) => <div data-testid="detail-stub">{proposalId}</div>,
}));
jest.mock('../../../../../telemetry/useScreenView', () => ({ useScreenView: () => undefined }));

const renderPage = (url = '/marketing/ads/proposals') =>
  render(
    <MemoryRouter initialEntries={[url]}>
      <MarketingAdsProposalsPage />
    </MemoryRouter>,
  );

describe('MarketingAdsProposalsPage', () => {
  beforeEach(() => {
    mockProposalsQuery.mockReturnValue({
      data: { items: [buildProposalSummary({ id: '7' }), buildProposalSummary({ id: '8', actionType: 'PauseAd' })], totalCount: 2 },
      isLoading: false,
      error: null,
    });
  });

  it('opens on the pending tab and asks only for pending proposals', () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Návrhy změn v reklamách' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Ke schválení' })).toHaveAttribute('aria-selected', 'true');
    expect(mockProposalsQuery).toHaveBeenLastCalledWith({ status: 'Pending', page: 1, pageSize: 25 });
  });

  it('the history tab asks for proposals in every status', () => {
    renderPage();

    fireEvent.click(screen.getByRole('tab', { name: 'Historie' }));

    expect(mockProposalsQuery).toHaveBeenLastCalledWith({ status: null, page: 1, pageSize: 25 });
  });

  it('selecting a proposal opens its detail', () => {
    renderPage();
    expect(screen.queryByTestId('detail-stub')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Pozastavení reklam/ }));

    expect(screen.getByTestId('detail-stub')).toHaveTextContent('8');
  });

  it('opens the detail named in the URL', () => {
    renderPage('/marketing/ads/proposals?proposal=42');

    expect(screen.getByTestId('detail-stub')).toHaveTextContent('42');
  });

  it('says so when nothing is waiting for approval', () => {
    mockProposalsQuery.mockReturnValue({ data: { items: [], totalCount: 0 }, isLoading: false, error: null });

    renderPage();

    expect(screen.getByTestId('proposal-list-empty')).toHaveTextContent('Nic nečeká na schválení.');
  });

  it('links to Metabase instead of drawing charts', () => {
    renderPage();

    expect(screen.getByRole('link', { name: /Výkon kampaní v Metabase/ })).toHaveAttribute('target', '_blank');
  });

  it('shows a load error', () => {
    mockProposalsQuery.mockReturnValue({ data: undefined, isLoading: false, error: new Error('Server nedostupný') });

    renderPage();

    expect(screen.getByText('Server nedostupný')).toBeInTheDocument();
  });

  it('steps back to the last page when a decision empties the current one', () => {
    // Arrange: 26 pending, so page 2 holds a single proposal.
    mockProposalsQuery.mockImplementation(({ page }: { page: number }) => ({
      data: page === 1 ? { items: [buildProposalSummary({ id: '7' })], totalCount: 26 } : { items: [], totalCount: 25 },
      isLoading: false,
      error: null,
    }));
    renderPage();

    // Act: go to page 2, which the decision has just emptied.
    fireEvent.click(screen.getByRole('button', { name: 'Další' }));

    // Assert
    expect(mockProposalsQuery).toHaveBeenCalledWith({ status: 'Pending', page: 2, pageSize: 25 });
    expect(mockProposalsQuery).toHaveBeenLastCalledWith({ status: 'Pending', page: 1, pageSize: 25 });
  });
});
