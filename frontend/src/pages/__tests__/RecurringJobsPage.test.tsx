import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import RecurringJobsPage from '../RecurringJobsPage';
import { RecurringJobCategory } from '../../api/generated/api-client';

jest.mock('../../telemetry/useScreenView', () => ({
  useScreenView: () => undefined,
}));

const mockJobs = [
  {
    jobName: 'daily-invoice-import-czk',
    displayName: 'Daily Invoice Import (CZK)',
    description: 'Imports invoices from previous day',
    cronExpression: '0 2 * * *',
    timeZoneId: 'Europe/Prague',
    isEnabled: true,
    category: RecurringJobCategory.Finance,
    lastModifiedAt: '2026-09-01T10:00:00Z',
    lastModifiedBy: 'System',
    nextRunAt: null,
  },
  {
    jobName: 'daily-comgate-czk-import',
    displayName: 'Daily Comgate CZK Import',
    description: 'Imports Comgate statements',
    cronExpression: '30 4 * * *',
    timeZoneId: 'Europe/Prague',
    isEnabled: true,
    category: RecurringJobCategory.Finance,
    lastModifiedAt: '2026-09-01T10:00:00Z',
    lastModifiedBy: 'System',
    nextRunAt: null,
  },
  {
    jobName: 'photobank-index',
    displayName: 'Photobank Index',
    description: 'Indexes photobank assets',
    cronExpression: '0 5 * * *',
    timeZoneId: 'Europe/Prague',
    isEnabled: false,
    category: RecurringJobCategory.Content,
    lastModifiedAt: '2026-09-01T10:00:00Z',
    lastModifiedBy: 'System',
    nextRunAt: null,
  },
];

// Module-level call logs: CRA's resetMocks strips jest.fn implementations between tests.
const statusCalls: unknown[] = [];
const triggerCalls: unknown[] = [];
const cronCalls: unknown[] = [];

jest.mock('../../api/hooks/useRecurringJobs', () => {
  const actual = jest.requireActual('../../api/generated/api-client');
  return {
    RecurringJobCategory: actual.RecurringJobCategory,
    useRecurringJobsQuery: () => ({
      data: mockJobs,
      isLoading: false,
      error: null,
      refetch: () => undefined,
    }),
    useUpdateRecurringJobStatusMutation: () => ({
      mutateAsync: (args: unknown) => { statusCalls.push(args); return Promise.resolve(); },
      isPending: false,
    }),
    useTriggerRecurringJobMutation: () => ({
      mutateAsync: (args: unknown) => { triggerCalls.push(args); return Promise.resolve(); },
      isPending: false,
    }),
    useUpdateRecurringJobCronMutation: () => ({
      mutateAsync: (args: unknown) => { cronCalls.push(args); return Promise.resolve(); },
      isPending: false,
    }),
  };
});

const getSearchInput = () => screen.getByRole('searchbox', { name: 'Hledat úlohu' });

describe('RecurringJobsPage grouping and filtering', () => {
  it('renders each category as a group header with its job count', () => {
    // Arrange & Act
    render(<RecurringJobsPage />);

    // Assert
    expect(screen.getByRole('button', { name: 'Sbalit kategorii Účetnictví a platby' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sbalit kategorii Obsah a AI' })).toBeInTheDocument();
    expect(screen.getByText('(2)')).toBeInTheDocument();
    expect(screen.getByText('(1)')).toBeInTheDocument();
  });

  it('shows every job expanded by default', () => {
    // Arrange & Act
    render(<RecurringJobsPage />);

    // Assert
    expect(screen.getByText('Daily Invoice Import (CZK)')).toBeInTheDocument();
    expect(screen.getByText('Daily Comgate CZK Import')).toBeInTheDocument();
    expect(screen.getByText('Photobank Index')).toBeInTheDocument();
  });

  it('narrows the visible jobs to those matching the search term', async () => {
    // Arrange
    render(<RecurringJobsPage />);

    // Act
    await userEvent.type(getSearchInput(), 'comgate');

    // Assert
    expect(screen.getByText('Daily Comgate CZK Import')).toBeInTheDocument();
    expect(screen.queryByText('Daily Invoice Import (CZK)')).not.toBeInTheDocument();
    expect(screen.queryByText('Photobank Index')).not.toBeInTheDocument();
  });

  it('hides a category whose jobs are all filtered out', async () => {
    // Arrange
    render(<RecurringJobsPage />);

    // Act
    await userEvent.type(getSearchInput(), 'comgate');

    // Assert
    expect(screen.getByRole('button', { name: 'Sbalit kategorii Účetnictví a platby' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /kategorii Obsah a AI/ })).not.toBeInTheDocument();
  });

  it('shows an empty state when no job matches the search term', async () => {
    // Arrange
    render(<RecurringJobsPage />);

    // Act
    await userEvent.type(getSearchInput(), 'nonexistent');

    // Assert
    expect(screen.getByText(/Žádná úloha neodpovídá hledání/)).toBeInTheDocument();
    expect(screen.queryByText('Photobank Index')).not.toBeInTheDocument();
  });

  it('collapses and re-expands a category when its header is clicked', async () => {
    // Arrange
    render(<RecurringJobsPage />);
    const header = () => screen.getByRole('button', { name: /kategorii Obsah a AI/ });

    // Act - collapse
    await userEvent.click(header());

    // Assert - the row is gone but the header remains, showing the count
    expect(screen.queryByText('Photobank Index')).not.toBeInTheDocument();
    expect(header()).toHaveAttribute('aria-expanded', 'false');

    // Act - re-expand
    await userEvent.click(header());

    // Assert
    expect(screen.getByText('Photobank Index')).toBeInTheDocument();
    expect(header()).toHaveAttribute('aria-expanded', 'true');
  });

  it('keeps other categories expanded when one is collapsed', async () => {
    // Arrange
    render(<RecurringJobsPage />);

    // Act
    await userEvent.click(screen.getByRole('button', { name: /kategorii Obsah a AI/ }));

    // Assert - only the collapsed category's rows disappear
    expect(screen.queryByText('Photobank Index')).not.toBeInTheDocument();
    expect(screen.getByText('Daily Invoice Import (CZK)')).toBeInTheDocument();
    expect(screen.getByText('Daily Comgate CZK Import')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Sbalit kategorii Účetnictví a platby' })
    ).toHaveAttribute('aria-expanded', 'true');
  });
});

describe('RecurringJobsPage search over collapsed categories', () => {
  it('shows matching jobs of a collapsed category while searching, and restores the collapse afterwards', async () => {
    // Arrange
    render(<RecurringJobsPage />);
    await userEvent.click(screen.getByRole('button', { name: /kategorii Obsah a AI/ }));
    expect(screen.queryByText('Photobank Index')).not.toBeInTheDocument();

    // Act - search for a job in the collapsed category
    await userEvent.type(getSearchInput(), 'photobank');

    // Assert - the match is visible despite the collapse
    expect(screen.getByText('Photobank Index')).toBeInTheDocument();

    // Act - clear the search
    await userEvent.clear(getSearchInput());

    // Assert - the user's collapse choice is back
    expect(screen.queryByText('Photobank Index')).not.toBeInTheDocument();
  });
});

describe('RecurringJobsPage row actions', () => {
  beforeEach(() => {
    statusCalls.length = 0;
    triggerCalls.length = 0;
    cronCalls.length = 0;
  });

  it('toggles the job status with the inverted enabled flag', async () => {
    // Arrange
    render(<RecurringJobsPage />);

    // Act
    await userEvent.click(screen.getByRole('switch', { name: 'Vypnout úlohu Daily Comgate CZK Import' }));

    // Assert
    expect(statusCalls).toEqual([{ jobName: 'daily-comgate-czk-import', isEnabled: false }]);
  });

  it('triggers the job only after the confirmation dialog is confirmed', async () => {
    // Arrange
    render(<RecurringJobsPage />);

    // Act
    await userEvent.click(screen.getByRole('button', { name: 'Spustit úlohu Photobank Index nyní' }));
    expect(triggerCalls).toEqual([]);
    await userEvent.click(screen.getByRole('button', { name: 'Spustit' }));

    // Assert
    expect(triggerCalls).toEqual(['photobank-index']);
  });

  it('saves the edited CRON expression on Enter', async () => {
    // Arrange
    render(<RecurringJobsPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Upravit CRON výraz pro Photobank Index' }));
    const input = screen.getByRole('textbox', { name: 'CRON výraz' });

    // Act
    await userEvent.clear(input);
    await userEvent.type(input, '0 6 * * *{Enter}');

    // Assert
    expect(cronCalls).toEqual([{ jobName: 'photobank-index', cronExpression: '0 6 * * *' }]);
  });

  it('cancels the CRON edit on Escape without saving', async () => {
    // Arrange
    render(<RecurringJobsPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Upravit CRON výraz pro Photobank Index' }));

    // Act
    await userEvent.type(screen.getByRole('textbox', { name: 'CRON výraz' }), '{Escape}');

    // Assert
    expect(cronCalls).toEqual([]);
    expect(screen.queryByRole('textbox', { name: 'CRON výraz' })).not.toBeInTheDocument();
  });
});
