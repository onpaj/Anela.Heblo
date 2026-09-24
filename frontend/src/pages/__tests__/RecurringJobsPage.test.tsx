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
    useUpdateRecurringJobStatusMutation: () => ({ mutateAsync: () => Promise.resolve(), isPending: false }),
    useTriggerRecurringJobMutation: () => ({ mutateAsync: () => Promise.resolve(), isPending: false }),
    useUpdateRecurringJobCronMutation: () => ({ mutateAsync: () => Promise.resolve(), isPending: false }),
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
