import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import MarketingAdsSettingsPage from '../MarketingAdsSettingsPage';
import type { AdsSettings } from '../../../../../api/hooks/marketingAds/types';

const mockSettingsQuery = jest.fn();
const mockKillSwitchMutate = jest.fn();
const mockAutonomyMutate = jest.fn();
const mockLimitMutate = jest.fn();
jest.mock('../../../../../api/hooks/useMarketingAdsSettings', () => ({
  useAdsSettingsQuery: (...args: unknown[]) => mockSettingsQuery(...args),
  useSetAdKillSwitchMutation: () => ({ mutate: mockKillSwitchMutate, isPending: false }),
  useUpdateAdAutonomyModeMutation: () => ({ mutate: mockAutonomyMutate, isPending: false }),
  useUpdateAdLimitMutation: () => ({ mutate: mockLimitMutate, isPending: false }),
}));
jest.mock('../../../../../telemetry/useScreenView', () => ({ useScreenView: () => undefined }));

// Plain mutable flag, not jest.fn(impl): CRA's resetMocks strips jest.fn implementations.
let mockIsAdmin = true;
jest.mock('../../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({ hasPermission: (perm: string) => mockIsAdmin && perm === 'marketing.ad_approvals.admin' }),
}));

const SETTINGS: AdsSettings = {
  killSwitch: { executionEnabled: false, platformOverrides: [{ platform: 'Sklik', executionEnabled: false }] },
  autonomy: [
    { platform: 'GoogleAds', actionType: 'AddNegativeKeyword', mode: 'ProposeOnly', approvedWithoutEditRate: 0.87, decidedCount: 23 },
    { platform: 'Sklik', actionType: 'PauseAd', mode: 'ProposeOnly', approvedWithoutEditRate: null, decidedCount: 0 },
  ],
  limits: [{ platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxActionsPerProposal', value: 10 }],
};

type MutateOptions = { onSuccess?: () => void; onError?: (error: unknown) => void };

describe('MarketingAdsSettingsPage', () => {
  beforeEach(() => {
    mockIsAdmin = true;
    mockSettingsQuery.mockReturnValue({ data: SETTINGS, isLoading: false, error: null });
  });

  it('refuses a non-admin and does not load settings', () => {
    mockIsAdmin = false;

    render(<MarketingAdsSettingsPage />);

    expect(screen.getByText('403 – Přístup odepřen')).toBeInTheDocument();
    expect(mockSettingsQuery).toHaveBeenLastCalledWith(false);
    expect(screen.queryByTestId('kill-switch')).not.toBeInTheDocument();
  });

  it('shows that execution is disabled and warns about it', () => {
    render(<MarketingAdsSettingsPage />);

    expect(screen.getByRole('heading', { name: 'Nastavení reklam' })).toBeInTheDocument();
    expect(screen.getByTestId('kill-switch')).toHaveTextContent('Hlavní vypínač je vypnutý');
  });

  it('explains that only the web login can turn execution on', () => {
    render(<MarketingAdsSettingsPage />);

    expect(screen.getByTestId('kill-switch')).toHaveTextContent('Zapnout lze jen po přihlášení ve webové aplikaci; vypnout může správce vždy.');
  });

  it('shows platforms without an override as enabled and an override as disabled', () => {
    render(<MarketingAdsSettingsPage />);

    expect(screen.getByRole('button', { name: 'Vypnout provádění: Google Ads' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Zapnout provádění: Sklik' })).toBeInTheDocument();
    expect(screen.getByTestId('kill-switch')).toHaveTextContent('bez vlastního nastavení');
  });

  it('enabling execution needs an explicit confirmation', () => {
    // Arrange
    mockKillSwitchMutate.mockImplementation((_vars: unknown, options: MutateOptions) => options.onSuccess?.());
    render(<MarketingAdsSettingsPage />);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Zapnout provádění: Všechny platformy' }));

    // Assert — nothing sent until confirmed
    expect(mockKillSwitchMutate).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog', { name: 'Zapnout provádění změn?' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Zapnout provádění' }));
    expect(mockKillSwitchMutate).toHaveBeenCalledWith({ platform: null, executionEnabled: true }, expect.anything());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('cancelling the confirmation sends nothing', () => {
    render(<MarketingAdsSettingsPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Vypnout provádění: Google Ads' }));
    fireEvent.click(screen.getByRole('button', { name: 'Zrušit' }));

    expect(mockKillSwitchMutate).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows a kill-switch error inline', () => {
    mockKillSwitchMutate.mockImplementation((_vars: unknown, options: MutateOptions) => options.onError?.(new Error('Uložení selhalo')));
    render(<MarketingAdsSettingsPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Zapnout provádění: Sklik' }));
    fireEvent.click(screen.getByRole('button', { name: 'Zapnout provádění' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Uložení selhalo');
  });

  it('shows the approval-without-edit rate next to each autonomy setting', () => {
    render(<MarketingAdsSettingsPage />);

    const table = screen.getByTestId('autonomy-table');
    expect(table).toHaveTextContent('87 % z 23');
    expect(table).toHaveTextContent('zatím bez rozhodnutí');
  });

  it('changing an autonomy mode is confirmed before it is sent', () => {
    render(<MarketingAdsSettingsPage />);

    fireEvent.change(screen.getByLabelText('Režim: Google Ads – Vylučující klíčová slova'), { target: { value: 'AutoWithinLimits' } });
    expect(mockAutonomyMutate).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Změnit režim' }));

    expect(mockAutonomyMutate).toHaveBeenCalledWith(
      { platform: 'GoogleAds', actionType: 'AddNegativeKeyword', mode: 'AutoWithinLimits' },
      expect.anything(),
    );
  });

  it('validates a limit before saving it', () => {
    render(<MarketingAdsSettingsPage />);
    const input = screen.getByLabelText('Limit: MaxActionsPerProposal (Google Ads – Pozastavení reklam)');
    const save = screen.getByRole('button', { name: 'Uložit limit MaxActionsPerProposal' });
    expect(save).toBeDisabled();

    fireEvent.change(input, { target: { value: '-3' } });
    expect(screen.getByText('Zadejte celé číslo 0 nebo větší.')).toBeInTheDocument();
    expect(save).toBeDisabled();

    fireEvent.change(input, { target: { value: '5' } });
    fireEvent.click(save);

    expect(mockLimitMutate).toHaveBeenCalledWith(
      { platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxActionsPerProposal', value: 5 },
      expect.anything(),
    );
  });
});
