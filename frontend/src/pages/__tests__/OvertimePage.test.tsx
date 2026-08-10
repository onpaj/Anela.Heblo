import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

jest.mock('../../auth/PermissionsContext', () => ({ usePermissionsContext: jest.fn() }));
jest.mock('../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { overtime: ['overtime'] },
}));
jest.mock('../../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }));
jest.mock('../../contexts/ToastContext', () => ({
  useToast: () => ({ showSuccess: jest.fn(), showError: jest.fn() }),
}));

import { getAuthenticatedApiClient } from '../../api/client';
import { usePermissionsContext } from '../../auth/PermissionsContext';
import OvertimePage from '../OvertimePage';

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <OvertimePage />
    </QueryClientProvider>,
  );
};

describe('OvertimePage', () => {
  beforeEach(() => {
    (usePermissionsContext as jest.Mock).mockReturnValue({
      hasPermission: (p: string) => ['attendance.overtime.read', 'attendance.overtime.write'].includes(p),
    });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      overtime_GetMonthlyStatements: jest.fn().mockResolvedValue({
        success: true, year: 2026, month: 7, isClosed: false,
        statements: [{
          personId: 'cccccccc-cccc-cccc-cccc-cccccccccccc', displayName: 'Pepina', isReviewed: false,
          requiredHours: 134.4, workedHours: 130, vacationHours: 6.4, sickHours: 0, doctorHours: 0,
          compTimeHours: 0, otherAbsenceHours: 0, deltaHours: 2, previousBalance: 2.5,
          adjustmentsTotal: 0, projectedBalance: 4.5, warnings: [], adjustments: [],
        }],
      }),
      overtime_GetEmployees: jest.fn().mockResolvedValue({ success: true, employees: [], availablePeople: [] }),
    });
  });

  test('renders statement row with projected balance', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Pepina')).toBeInTheDocument());
    expect(screen.getByText('Evidence přesčasů')).toBeInTheDocument();
    expect(screen.getByText(/4,5|4.5/)).toBeInTheDocument();
  });

  test('shows close button for writer on open month', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Pepina')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Uzavřít měsíc/ })).toBeInTheDocument();
  });
});
