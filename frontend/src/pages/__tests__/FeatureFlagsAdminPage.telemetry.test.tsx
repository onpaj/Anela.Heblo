import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FeatureFlagsAdminPage from '../FeatureFlagsAdminPage';

const mockTrackEvent = jest.fn();
jest.mock('../../telemetry/useTelemetry', () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const mockUpsertMutate = jest.fn();
jest.mock('../../api/hooks/useFeatureFlagsAdmin', () => ({
  useFeatureFlagsAdmin: () => ({
    data: [
      {
        key: 'show-new-dashboard',
        description: 'Show the new dashboard layout',
        currentValue: false,
        defaultValue: false,
        isOverridden: false,
        updatedBy: null,
        updatedAt: null,
      },
    ],
    isLoading: false,
    error: null,
  }),
  useUpsertFlagOverride: () => ({ mutate: mockUpsertMutate, isPending: false }),
  useClearFlagOverride: () => ({ mutate: jest.fn(), isPending: false }),
}));

describe('FeatureFlagsAdminPage telemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('tracks FeatureFlagToggled with flagKey and enabled when toggle is clicked', async () => {
    render(<FeatureFlagsAdminPage />);

    // The toggle button has aria-label="Toggle show-new-dashboard"
    const toggle = screen.getByRole('button', { name: /toggle show-new-dashboard/i });
    await userEvent.click(toggle);

    await waitFor(() => {
      expect(mockTrackEvent).toHaveBeenCalledWith('FeatureFlagToggled', {
        flagKey: 'show-new-dashboard',
        enabled: 'true',
      });
    });
  });

  it('also calls upsert.mutate when toggle is clicked', async () => {
    render(<FeatureFlagsAdminPage />);

    const toggle = screen.getByRole('button', { name: /toggle show-new-dashboard/i });
    await userEvent.click(toggle);

    await waitFor(() => {
      expect(mockUpsertMutate).toHaveBeenCalledWith({
        key: 'show-new-dashboard',
        isEnabled: true,
      });
    });
  });
});
