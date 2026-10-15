import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MarketingAdsActivityPage from '../MarketingAdsActivityPage';

const mockAuditQuery = jest.fn();
const mockChangesQuery = jest.fn();
const mockRunsQuery = jest.fn();
jest.mock('../../../../../api/hooks/useMarketingAdsActivity', () => ({
  useAdAuditEventsQuery: (...args: unknown[]) => mockAuditQuery(...args),
  useAdChangeEventsQuery: (...args: unknown[]) => mockChangesQuery(...args),
  useAgentRunsQuery: (...args: unknown[]) => mockRunsQuery(...args),
}));
jest.mock('../../../../../telemetry/useScreenView', () => ({ useScreenView: () => undefined }));

const page = <T,>(items: T[]) => ({ data: { items, totalCount: items.length }, isLoading: false, error: null });

const renderPage = () =>
  render(
    <MemoryRouter>
      <MarketingAdsActivityPage />
    </MemoryRouter>,
  );

describe('MarketingAdsActivityPage', () => {
  beforeEach(() => {
    mockAuditQuery.mockReturnValue(
      page([
        {
          id: '1', proposalId: '7', version: 2, eventType: 'Approved', actorPrincipal: 'ondra@anela.cz',
          actorKind: 'Human', channel: 'Mcp', occurredAt: new Date('2026-10-07T09:00:00Z'), payloadJson: null,
        },
      ]),
    );
    mockChangesQuery.mockReturnValue(
      page([
        {
          id: '5', platform: 'GoogleAds', accountExternalId: '123', occurredAt: new Date('2026-10-06T12:00:00Z'),
          actor: 'agentura@example.com', actorKind: 'User', entityLevel: 'Campaign', entityExternalId: '111',
          entityName: 'Brand CZ', changeType: 'BudgetChanged', oldValue: '1000', newValue: '1500',
          source: 'PlatformChangeLog', origin: 'OutOfBand',
        },
      ]),
    );
    mockRunsQuery.mockReturnValue(
      page([
        {
          id: '3', agentName: 'paid-search', principal: 'heblo-marketing-agent', startedAt: new Date('2026-10-07T05:00:00Z'),
          finishedAt: null, status: 'Running', summary: 'Prochází hledané výrazy', proposalsCreated: 2,
          approxCostCzk: 4.5, approxTokens: 120000,
        },
      ]),
    );
  });

  it('opens on the audit log, loading only that tab', () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Aktivita v reklamách' })).toBeInTheDocument();
    expect(mockAuditQuery).toHaveBeenLastCalledWith({ page: 1, pageSize: 50 }, true);
    expect(mockChangesQuery).toHaveBeenLastCalledWith(expect.anything(), false);
    expect(mockRunsQuery).toHaveBeenLastCalledWith(expect.anything(), false);
    const table = screen.getByTestId('audit-table');
    expect(table).toHaveTextContent('Schváleno');
    expect(table).toHaveTextContent('Claude (MCP)');
    expect(within(table).getByRole('link', { name: '#7' })).toHaveAttribute('href', '/marketing/ads/proposals?tab=history&proposal=7');
  });

  it('shows out-of-band changes by default on the changes tab', () => {
    renderPage();

    fireEvent.click(screen.getByRole('tab', { name: 'Změny v účtech' }));

    expect(mockChangesQuery).toHaveBeenLastCalledWith(
      { platform: null, origin: 'OutOfBand', actorKind: null, page: 1, pageSize: 50 },
      true,
    );
    const table = screen.getByTestId('change-events-table');
    expect(table).toHaveTextContent('Mimo Heblo');
    expect(table).toHaveTextContent('agentura@example.com');
    expect(table).toHaveTextContent('kampaň Brand CZ');
  });

  it('tells the user the changes tab covers the last 30 days', () => {
    renderPage();

    fireEvent.click(screen.getByRole('tab', { name: 'Změny v účtech' }));

    expect(screen.getByText('Posledních 30 dní.')).toBeInTheDocument();
  });

  it('labels a system actor on the audit log', () => {
    mockAuditQuery.mockReturnValue(
      page([
        {
          id: '2', proposalId: null, version: null, eventType: 'Approved', actorPrincipal: 'scheduler',
          actorKind: 'System', channel: 'System', occurredAt: new Date('2026-10-07T09:00:00Z'), payloadJson: null,
        },
      ]),
    );

    renderPage();

    expect(screen.getByTestId('audit-table')).toHaveTextContent('scheduler (Systém)');
  });

  it('renders old and new values as plain text, never as markup', () => {
    mockChangesQuery.mockReturnValue(
      page([
        {
          id: '6', platform: 'Sklik', accountExternalId: '9', occurredAt: null, actor: null, actorKind: 'Unknown',
          entityLevel: null, entityExternalId: null, entityName: null, changeType: 'Edited',
          oldValue: '<img src=x onerror=alert(1)>', newValue: '{"a":1}', source: 'SnapshotDiff', origin: 'OutOfBand',
        },
      ]),
    );
    renderPage();

    fireEvent.click(screen.getByRole('tab', { name: 'Změny v účtech' }));

    const table = screen.getByTestId('change-events-table');
    expect(table).toHaveTextContent('<img src=x onerror=alert(1)> → {"a":1}');
    expect(within(table).queryByRole('img')).not.toBeInTheDocument();
  });

  it('filters changes by platform and actor kind', () => {
    renderPage();
    fireEvent.click(screen.getByRole('tab', { name: 'Změny v účtech' }));

    fireEvent.change(screen.getByLabelText('Platforma'), { target: { value: 'Sklik' } });
    fireEvent.change(screen.getByLabelText('Aktér'), { target: { value: 'PlatformAutomation' } });

    expect(mockChangesQuery).toHaveBeenLastCalledWith(
      { platform: 'Sklik', origin: 'OutOfBand', actorKind: 'PlatformAutomation', page: 1, pageSize: 50 },
      true,
    );
  });

  it('can show all origins', () => {
    renderPage();
    fireEvent.click(screen.getByRole('tab', { name: 'Změny v účtech' }));

    fireEvent.change(screen.getByLabelText('Původ'), { target: { value: '' } });

    expect(mockChangesQuery).toHaveBeenLastCalledWith(expect.objectContaining({ origin: null }), true);
  });

  it('lists agent runs with their cost', () => {
    renderPage();

    fireEvent.click(screen.getByRole('tab', { name: 'Běhy agentů' }));

    const table = screen.getByTestId('agent-runs-table');
    expect(table).toHaveTextContent('paid-search');
    expect(table).toHaveTextContent('Běží');
    expect(table).toHaveTextContent('120 000 tokenů');
  });

  it('shows an empty state per tab', () => {
    mockAuditQuery.mockReturnValue(page([]));

    renderPage();

    expect(screen.getByText('Zatím žádné záznamy.')).toBeInTheDocument();
  });
});
