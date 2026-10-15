import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import ProposalDecisionBar from '../ProposalDecisionBar';
import { SwaggerException } from '../../../../../api/generated/api-client';
import { buildProposalDetail } from '../../__fixtures__/adsFixtures';
import type { AdsSettings } from '../../../../../api/hooks/marketingAds/types';

const mockApproveMutate = jest.fn();
const mockRejectMutate = jest.fn();
const mockRevertMutate = jest.fn();
let mockIsApprovePending = false;
jest.mock('../../../../../api/hooks/useMarketingAdsProposals', () => ({
  useApproveAdProposalMutation: () => ({ mutate: mockApproveMutate, isPending: mockIsApprovePending }),
  useRejectAdProposalMutation: () => ({ mutate: mockRejectMutate, isPending: false }),
  useRevertAdProposalMutation: () => ({ mutate: mockRevertMutate, isPending: false }),
}));

// Plain mutable list, not jest.fn(impl): CRA's resetMocks strips jest.fn implementations before each test.
let mockPermissions: string[] = [];
jest.mock('../../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({ hasPermission: (perm: string) => mockPermissions.includes(perm) }),
}));

// Same reason: plain mutable state. The mock honours `enabled` like react-query (no data when disabled).
let mockSettingsData: AdsSettings | undefined;
let mockSettingsError: Error | null = null;
let mockSettingsEnabled: boolean | undefined;
jest.mock('../../../../../api/hooks/useMarketingAdsSettings', () => ({
  useAdsSettingsQuery: (enabled: boolean) => {
    mockSettingsEnabled = enabled;
    return enabled ? { data: mockSettingsData, error: mockSettingsError } : { data: undefined, error: null };
  },
}));

const APPROVER = ['marketing.ads.read', 'marketing.ad_approvals.write'];
const ADMIN = [...APPROVER, 'marketing.ad_approvals.admin'];

const EXECUTION_DISABLED_TEXT =
  'Provádění změn je vypnuté (hlavní vypínač v Nastavení reklam). Schválený návrh se teď neprovede a skončí jako „Selhalo“.';

const buildSettings = (executionEnabled: boolean, platformOverrides: AdsSettings['killSwitch']['platformOverrides'] = []): AdsSettings => ({
  autonomy: [],
  limits: [],
  killSwitch: { executionEnabled, platformOverrides },
});

type MutateOptions = { onSuccess?: () => void; onError?: (error: unknown) => void; onSettled?: () => void };

const swaggerWithBody = (status: number, errorCode: string) =>
  new SwaggerException('Error', status, JSON.stringify({ success: false, errorCode, params: null }), {}, null);

describe('ProposalDecisionBar', () => {
  beforeEach(() => {
    mockPermissions = APPROVER;
    mockIsApprovePending = false;
    mockSettingsData = buildSettings(true);
    mockSettingsError = null;
    mockSettingsEnabled = undefined;
  });

  it('approves exactly the rendered id and version', () => {
    render(<ProposalDecisionBar proposal={buildProposalDetail({ id: '7', currentVersion: 3 })} onRefresh={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Schválit verzi 3' }));

    expect(mockApproveMutate).toHaveBeenCalledWith({ id: '7', version: 3 }, expect.anything());
  });

  it('disables both decision buttons while an approval is in flight', () => {
    mockIsApprovePending = true;

    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

    expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Zamítnout' })).toBeDisabled();
  });

  it('shows the stale-version banner on 409 and blocks approval until refresh', () => {
    // Arrange
    const onRefresh = jest.fn();
    mockApproveMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.(new SwaggerException('Conflict', 409, '', {}, null)),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={onRefresh} />);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Schválit verzi 2' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Návrh se mezitím změnil');
    expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Obnovit detail' }));
    expect(onRefresh).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('treats a typed conflict DTO without an HTTP status as stale too', () => {
    mockApproveMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.({ success: false, errorCode: 'MarketingAdsProposalVersionMismatch', params: null }),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Schválit verzi 2' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Návrh se mezitím změnil');
  });

  it('a 409 carrying MarketingAdsAlreadyApprovedByPrincipal shows its message, not the stale banner', () => {
    // Arrange
    mockApproveMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.(swaggerWithBody(409, 'MarketingAdsAlreadyApprovedByPrincipal')),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Schválit verzi 2' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Tuto verzi návrhu už jste schválili.');
    expect(screen.queryByRole('button', { name: 'Obnovit detail' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeEnabled();
  });

  it('explains a 403 instead of showing the stale banner', () => {
    mockApproveMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.(new SwaggerException('Forbidden', 403, '', {}, null)),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Schválit verzi 2' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Tento návrh nemůžete schválit');
    expect(screen.queryByRole('button', { name: 'Obnovit detail' })).not.toBeInTheDocument();
  });

  it('a typed MarketingAdsApprovalChannelDenied DTO shows the Czech channel-denied text', () => {
    mockApproveMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.({ success: false, errorCode: 'MarketingAdsApprovalChannelDenied' }),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Schválit verzi 2' }));

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Z tohoto přístupu nelze návrhy schvalovat, zamítat ani měnit nastavení.',
    );
    expect(screen.queryByRole('button', { name: 'Obnovit detail' })).not.toBeInTheDocument();
  });

  it('a 403 on reject does not claim the user tried to approve', () => {
    // Arrange
    mockRejectMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.(new SwaggerException('Forbidden', 403, '', {}, null)),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Zamítnout' }));
    fireEvent.change(screen.getByLabelText('Důvod zamítnutí'), { target: { value: 'Mimo strategii' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Zamítnout návrh' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Tento návrh nemůžete zamítnout: chybí oprávnění Schvalování kampaní.');
    expect(screen.getByRole('alert')).not.toHaveTextContent('schválit');
  });

  it('a 403 on revert explains the missing admin permission', () => {
    // Arrange
    mockPermissions = ADMIN;
    mockRevertMutate.mockImplementation((_vars: unknown, options: MutateOptions) =>
      options.onError?.(new SwaggerException('Forbidden', 403, '', {}, null)),
    );
    render(<ProposalDecisionBar proposal={buildProposalDetail({ status: 'Executed' })} onRefresh={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Vrátit zpět' }));

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Vrátit změny' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Změny nemůžete vrátit zpět: chybí oprávnění správce Schvalování kampaní.');
  });

  it('disables the decisions while the detail is refetching', () => {
    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} isRefreshing />);

    expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Zamítnout' })).toBeDisabled();
  });

  describe('double-submit guard', () => {
    it('two synchronous approve clicks send one approval', () => {
      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);
      const approveButton = screen.getByRole('button', { name: 'Schválit verzi 2' });

      fireEvent.click(approveButton);
      fireEvent.click(approveButton);

      expect(mockApproveMutate).toHaveBeenCalledTimes(1);
    });

    it('allows approving again once the previous attempt has settled', () => {
      // Arrange
      mockApproveMutate.mockImplementation((_vars: unknown, options: MutateOptions) => {
        options.onError?.(new Error('Síť'));
        options.onSettled?.();
      });
      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);
      const approveButton = screen.getByRole('button', { name: 'Schválit verzi 2' });

      // Act
      fireEvent.click(approveButton);
      fireEvent.click(approveButton);

      // Assert
      expect(mockApproveMutate).toHaveBeenCalledTimes(2);
    });

    it('two synchronous reject confirmations send one rejection', () => {
      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);
      fireEvent.click(screen.getByRole('button', { name: 'Zamítnout' }));
      fireEvent.change(screen.getByLabelText('Důvod zamítnutí'), { target: { value: 'Mimo strategii' } });
      const confirm = screen.getByRole('button', { name: 'Zamítnout návrh' });

      fireEvent.click(confirm);
      fireEvent.click(confirm);

      expect(mockRejectMutate).toHaveBeenCalledTimes(1);
    });

    it('two synchronous revert confirmations send one revert', () => {
      mockPermissions = ADMIN;
      render(<ProposalDecisionBar proposal={buildProposalDetail({ status: 'Executed' })} onRefresh={jest.fn()} />);
      fireEvent.click(screen.getByRole('button', { name: 'Vrátit zpět' }));
      const confirm = screen.getByRole('button', { name: 'Vrátit změny' });

      fireEvent.click(confirm);
      fireEvent.click(confirm);

      expect(mockRevertMutate).toHaveBeenCalledTimes(1);
    });
  });

  it('hides decisions and explains why for a user without the approve permission', () => {
    mockPermissions = ['marketing.ads.read'];

    render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

    expect(screen.queryByRole('button', { name: 'Schválit verzi 2' })).not.toBeInTheDocument();
    expect(screen.getByText(/oprávnění Schvalování kampaní/)).toBeInTheDocument();
  });

  it('disables approve when the limits engine denied the proposal', () => {
    render(
      <ProposalDecisionBar
        proposal={buildProposalDetail({ limits: { outcome: 'Denied', reason: 'Účet není spravovaný' } })}
        onRefresh={jest.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Zamítnout' })).toBeEnabled();
  });

  it('requires a reason before a rejection can be sent', () => {
    // Arrange
    mockRejectMutate.mockImplementation((_vars: unknown, options: MutateOptions) => options.onSuccess?.());
    render(<ProposalDecisionBar proposal={buildProposalDetail({ id: '7', currentVersion: 2 })} onRefresh={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Zamítnout' }));
    const confirm = screen.getByRole('button', { name: 'Zamítnout návrh' });
    expect(confirm).toBeDisabled();

    // Act
    fireEvent.change(screen.getByLabelText('Důvod zamítnutí'), { target: { value: '  Mimo strategii  ' } });
    fireEvent.click(confirm);

    // Assert
    expect(mockRejectMutate).toHaveBeenCalledWith({ id: '7', version: 2, reason: 'Mimo strategii' }, expect.anything());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('offers revert to admins on an executed proposal, behind a confirmation', () => {
    mockPermissions = ADMIN;
    render(<ProposalDecisionBar proposal={buildProposalDetail({ id: '7', status: 'Executed' })} onRefresh={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Vrátit zpět' }));
    expect(mockRevertMutate).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Vrátit změny' }));

    expect(mockRevertMutate).toHaveBeenCalledWith({ id: '7' }, expect.anything());
  });

  it('does not offer revert to a non-admin approver', () => {
    render(<ProposalDecisionBar proposal={buildProposalDetail({ status: 'Executed' })} onRefresh={jest.fn()} />);

    expect(screen.queryByRole('button', { name: 'Vrátit zpět' })).not.toBeInTheDocument();
  });

  describe('execution kill switch warning', () => {
    it('warns next to approve when execution is switched off globally, approve stays enabled', () => {
      mockSettingsData = buildSettings(false);

      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

      expect(screen.getByTestId('execution-disabled-warning')).toHaveTextContent(EXECUTION_DISABLED_TEXT);
      expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeEnabled();
    });

    it('warns when only the override for the proposal platform is off', () => {
      mockSettingsData = buildSettings(true, [
        { platform: 'MetaAds', executionEnabled: true },
        { platform: 'GoogleAds', executionEnabled: false },
      ]);

      render(<ProposalDecisionBar proposal={buildProposalDetail({ platform: 'GoogleAds' })} onRefresh={jest.fn()} />);

      expect(screen.getByTestId('execution-disabled-warning')).toHaveTextContent(EXECUTION_DISABLED_TEXT);
    });

    it('does not warn when another platform is off but the proposal platform is on', () => {
      mockSettingsData = buildSettings(true, [{ platform: 'MetaAds', executionEnabled: false }]);

      render(<ProposalDecisionBar proposal={buildProposalDetail({ platform: 'GoogleAds' })} onRefresh={jest.fn()} />);

      expect(screen.queryByTestId('execution-disabled-warning')).not.toBeInTheDocument();
    });

    it('does not warn when execution is on', () => {
      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

      expect(screen.queryByTestId('execution-disabled-warning')).not.toBeInTheDocument();
      expect(mockSettingsEnabled).toBe(true);
    });

    it('does not load settings or warn for a proposal that is no longer pending', () => {
      mockSettingsData = buildSettings(false);

      render(<ProposalDecisionBar proposal={buildProposalDetail({ status: 'Executed' })} onRefresh={jest.fn()} />);

      expect(mockSettingsEnabled).toBe(false);
      expect(screen.queryByTestId('execution-disabled-warning')).not.toBeInTheDocument();
    });

    it('does not load settings for a user who cannot approve', () => {
      mockPermissions = ['marketing.ads.read'];
      mockSettingsData = buildSettings(false);

      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

      expect(mockSettingsEnabled).toBe(false);
      expect(screen.queryByTestId('execution-disabled-warning')).not.toBeInTheDocument();
    });

    it('stays quiet when the settings fail to load', () => {
      mockSettingsData = undefined;
      mockSettingsError = new Error('Network down');

      render(<ProposalDecisionBar proposal={buildProposalDetail()} onRefresh={jest.fn()} />);

      expect(screen.queryByTestId('execution-disabled-warning')).not.toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Schválit verzi 2' })).toBeEnabled();
    });
  });
});
