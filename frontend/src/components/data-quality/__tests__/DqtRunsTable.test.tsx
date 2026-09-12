import React from 'react';
import { render, screen } from '@testing-library/react';
import DqtRunsTable from '../DqtRunsTable';
import { useDqtRuns } from '../../../api/hooks/useDataQuality';

jest.mock('../../../api/hooks/useDataQuality', () => ({
  useDqtRuns: jest.fn(),
}));

const mockUseDqtRuns = useDqtRuns as jest.Mock;

test('renders the Czech label for a PriceComparison run instead of the raw enum name', () => {
  // Arrange
  mockUseDqtRuns.mockReturnValue({
    data: {
      items: [
        {
          id: 'run-1',
          testType: 'PriceComparison',
          status: 'Completed',
          dateFrom: new Date('2026-09-01'),
          dateTo: new Date('2026-09-01'),
          startedAt: new Date('2026-09-01T02:00:00Z'),
          totalChecked: 10,
          totalMismatches: 2,
          triggerType: 'Scheduled',
        },
      ],
      totalCount: 1,
      totalPages: 1,
    },
    isLoading: false,
    error: null,
  });

  // Act
  render(<DqtRunsTable onRunSelect={jest.fn()} selectedRunId={null} />);

  // Assert
  expect(screen.getByText('Kontrola cen')).toBeInTheDocument();
  expect(screen.queryByText('PriceComparison')).not.toBeInTheDocument();
});
