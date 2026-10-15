import React, { useRef, useState } from 'react';
import { AlertTriangle, RefreshCw } from 'lucide-react';
import { usePermissionsContext } from '../../../../auth/PermissionsContext';
import {
  useApproveAdProposalMutation,
  useRejectAdProposalMutation,
  useRevertAdProposalMutation,
} from '../../../../api/hooks/useMarketingAdsProposals';
import { useAdsSettingsQuery } from '../../../../api/hooks/useMarketingAdsSettings';
import { describeAdsError, isForbidden, isVersionConflict } from '../../../../api/hooks/marketingAds/errors';
import {
  LIMITS_OUTCOME,
  PROPOSAL_STATUS,
  type AdProposalDetail,
  type AdsSettings,
} from '../../../../api/hooks/marketingAds/types';
import { resolveSwaggerErrorMessage } from '../../../../utils/errorHandler';
import { ADS_PERMISSIONS } from '../shared/permissions';
import ConfirmActionDialog from '../shared/ConfirmActionDialog';

interface ProposalDecisionBarProps {
  proposal: AdProposalDetail;
  onRefresh: () => void;
  /** The detail is refetching (after a decision or "Obnovit detail"): decisions wait for the fresh version. */
  isRefreshing?: boolean;
}

type OpenDialog = 'reject' | 'revert' | null;

const APPROVE_FORBIDDEN_MESSAGE =
  'Tento návrh nemůžete schválit: chybí oprávnění Schvalování kampaní, nebo jste ho už schválili a je potřeba jiný schvalovatel.';
const REJECT_FORBIDDEN_MESSAGE = 'Tento návrh nemůžete zamítnout: chybí oprávnění Schvalování kampaní.';
const REVERT_FORBIDDEN_MESSAGE = 'Změny nemůžete vrátit zpět: chybí oprávnění správce Schvalování kampaní.';
const EXECUTION_DISABLED_MESSAGE =
  'Provádění změn je vypnuté (hlavní vypínač v Nastavení reklam). Schválený návrh se teď neprovede a skončí jako „Selhalo“.';
const REVERTIBLE_STATUSES: readonly string[] = [PROPOSAL_STATUS.Executed, PROPOSAL_STATUS.PartiallyExecuted];

const PRIMARY_BUTTON =
  'px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed';
const SECONDARY_BUTTON =
  'px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed';

// Fail quiet: no settings (still loading, or failed to load) means no warning — approve itself is unchanged.
const isExecutionOffFor = (settings: AdsSettings | undefined, platform: string): boolean => {
  if (!settings) {
    return false;
  }
  const { executionEnabled, platformOverrides } = settings.killSwitch;
  return !executionEnabled || platformOverrides.some((o) => o.platform === platform && !o.executionEnabled);
};

const ProposalDecisionBar: React.FC<ProposalDecisionBarProps> = ({ proposal, onRefresh, isRefreshing = false }) => {
  const { hasPermission } = usePermissionsContext();
  const approve = useApproveAdProposalMutation();
  const reject = useRejectAdProposalMutation();
  const revert = useRevertAdProposalMutation();
  const [openDialog, setOpenDialog] = useState<OpenDialog>(null);
  const [reason, setReason] = useState('');
  const [isStale, setIsStale] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // Synchronous guard: a fast double click lands before the isPending re-render disables the button.
  const isInFlight = useRef(false);

  const canApprove = hasPermission(ADS_PERMISSIONS.approve);
  const canRevert = hasPermission(ADS_PERMISSIONS.admin);
  const isPending = proposal.status === PROPOSAL_STATUS.Pending;
  const isDenied = proposal.limits?.outcome === LIMITS_OUTCOME.Denied;
  const canDecide = isPending && canApprove;
  const settings = useAdsSettingsQuery(canDecide);
  // Gated on canDecide too: a disabled query can still hand back data cached by the settings page.
  const isExecutionDisabled = canDecide && isExecutionOffFor(settings.data, proposal.platform);
  const isBusy = approve.isPending || reject.isPending || revert.isPending || isRefreshing;
  // The version the user is looking at — never re-read at click time (spec 6.2: approve names id + version).
  const decision = { id: proposal.id, version: proposal.currentVersion };

  const handleError = (error: unknown, forbiddenMessage: string) => {
    if (isVersionConflict(error)) {
      setIsStale(true);
      setErrorMessage(null);
      return;
    }
    // The specific Czech texts (channel denied, needs web approval…) win over the generic forbidden one.
    setErrorMessage(isForbidden(error) ? resolveSwaggerErrorMessage(error) ?? forbiddenMessage : describeAdsError(error));
  };

  /** Claims the in-flight slot; false when a request is already on its way. */
  const startRequest = (): boolean => {
    if (isInFlight.current) {
      return false;
    }
    isInFlight.current = true;
    setErrorMessage(null);
    return true;
  };

  const endRequest = () => {
    isInFlight.current = false;
  };

  const closeDialog = () => {
    setOpenDialog(null);
    setReason('');
  };

  const dialogCallbacks = (forbiddenMessage: string) => ({
    onSuccess: closeDialog,
    onError: (error: unknown) => {
      closeDialog();
      handleError(error, forbiddenMessage);
    },
    onSettled: endRequest,
  });

  const handleApprove = () => {
    if (!startRequest()) {
      return;
    }
    approve.mutate(decision, {
      onError: (error) => handleError(error, APPROVE_FORBIDDEN_MESSAGE),
      onSettled: endRequest,
    });
  };

  const handleConfirmReject = () => {
    if (startRequest()) {
      reject.mutate({ ...decision, reason: reason.trim() }, dialogCallbacks(REJECT_FORBIDDEN_MESSAGE));
    }
  };

  const handleConfirmRevert = () => {
    if (startRequest()) {
      revert.mutate({ id: proposal.id }, dialogCallbacks(REVERT_FORBIDDEN_MESSAGE));
    }
  };

  const handleRefresh = () => {
    setIsStale(false);
    setErrorMessage(null);
    onRefresh();
  };

  return (
    <section aria-label="Rozhodnutí o návrhu" className="space-y-3">
      {isStale && (
        <div role="alert" className="flex flex-wrap items-center gap-3 p-3 rounded-md border border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-900/30 text-sm text-amber-900 dark:text-amber-200">
          <AlertTriangle className="h-4 w-4 flex-shrink-0" aria-hidden="true" />
          <span className="flex-1">
            Návrh se mezitím změnil — existuje novější verze, nebo už byl vyřízen. Obnovte detail a zkontrolujte změny znovu.
          </span>
          <button type="button" onClick={handleRefresh} className={SECONDARY_BUTTON}>
            <RefreshCw className="inline h-4 w-4 mr-1" aria-hidden="true" />
            Obnovit detail
          </button>
        </div>
      )}

      {errorMessage && (
        <div role="alert" className="p-3 rounded-md border border-red-200 dark:border-red-900/50 bg-red-50 dark:bg-red-900/30 text-sm text-red-700 dark:text-red-300">
          {errorMessage}
        </div>
      )}

      {isExecutionDisabled && (
        <div
          data-testid="execution-disabled-warning"
          className="flex items-start gap-2 p-3 rounded-md border border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-900/30 text-sm text-amber-900 dark:text-amber-200"
        >
          <AlertTriangle className="h-4 w-4 mt-0.5 flex-shrink-0" aria-hidden="true" />
          <span>{EXECUTION_DISABLED_MESSAGE}</span>
        </div>
      )}

      {canDecide && (
        <div className="flex flex-wrap items-center gap-3">
          <button type="button" onClick={handleApprove} disabled={isBusy || isStale || isDenied} className={PRIMARY_BUTTON}>
            {`Schválit verzi ${proposal.currentVersion}`}
          </button>
          <button type="button" onClick={() => setOpenDialog('reject')} disabled={isBusy || isStale} className={SECONDARY_BUTTON}>
            Zamítnout
          </button>
          {isDenied && (
            <span className="text-sm text-red-700 dark:text-red-300">Limity návrh zamítly — schválit ho nelze.</span>
          )}
        </div>
      )}

      {isPending && !canApprove && (
        <p className="text-sm text-gray-600 dark:text-graphite-muted">
          Ke schválení nebo zamítnutí potřebujete oprávnění Schvalování kampaní.
        </p>
      )}

      {REVERTIBLE_STATUSES.includes(proposal.status) && canRevert && (
        <button type="button" onClick={() => setOpenDialog('revert')} disabled={isBusy} className={SECONDARY_BUTTON}>
          Vrátit zpět
        </button>
      )}

      <ConfirmActionDialog
        isOpen={openDialog === 'reject'}
        title="Zamítnout návrh?"
        message={`Zamítáte verzi ${proposal.currentVersion}. Autor uvidí váš důvod.`}
        confirmLabel="Zamítnout návrh"
        tone="danger"
        isPending={reject.isPending}
        isConfirmDisabled={reason.trim().length === 0}
        onConfirm={handleConfirmReject}
        onCancel={closeDialog}
      >
        <label htmlFor="ads-reject-reason" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
          Důvod zamítnutí
        </label>
        <textarea
          id="ads-reject-reason"
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          rows={3}
          className="w-full rounded-md border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface-2 text-sm text-gray-900 dark:text-graphite-text dark:placeholder-graphite-faint p-2"
        />
      </ConfirmActionDialog>

      <ConfirmActionDialog
        isOpen={openDialog === 'revert'}
        title="Vrátit provedené změny?"
        message="Heblo obnoví u každé provedené akce původní stav uložený před provedením."
        confirmLabel="Vrátit změny"
        tone="danger"
        isPending={revert.isPending}
        onConfirm={handleConfirmRevert}
        onCancel={closeDialog}
      />
    </section>
  );
};

export default ProposalDecisionBar;
