import React from 'react';
import ErrorState from '../../../common/ErrorState';
import { useAdProposalQuery, useRefreshAdProposals } from '../../../../api/hooks/useMarketingAdsProposals';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import { formatDateTime } from '../../../../utils/formatters';
import { ACTION_TYPE_LABELS, CREATOR_KIND_LABELS, labelFor } from '../shared/adLabels';
import { NEUTRAL_PILL, Pill, PlatformBadge, ProposalStatusBadge } from '../shared/Badges';
import AdsLoading from '../shared/AdsLoading';
import AgentReasoningBlock from './AgentReasoningBlock';
import LimitsEvaluationBadge from './LimitsEvaluationBadge';
import ProposalDecisionBar from './ProposalDecisionBar';
import ProposalDiff from './ProposalDiff';
import ProposalTimeline from './ProposalTimeline';

interface ProposalDetailProps {
  proposalId: string;
}

const META = 'text-xs text-gray-600 dark:text-graphite-muted';

const ProposalDetail: React.FC<ProposalDetailProps> = ({ proposalId }) => {
  const query = useAdProposalQuery(proposalId);
  const refresh = useRefreshAdProposals(proposalId);

  if (query.isLoading) {
    return <AdsLoading />;
  }
  if (query.error) {
    return <ErrorState message={describeAdsError(query.error)} className="py-8" />;
  }
  const proposal = query.data;
  if (!proposal) {
    return null;
  }

  return (
    <article data-testid="proposal-detail" className="space-y-4 p-4">
      <header className="space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-base font-semibold text-gray-900 dark:text-graphite-text">
            {labelFor(ACTION_TYPE_LABELS, proposal.actionType)}
          </h2>
          <PlatformBadge platform={proposal.platform} />
          <ProposalStatusBadge status={proposal.status} />
          <Pill className={NEUTRAL_PILL}>{`verze ${proposal.currentVersion}`}</Pill>
        </div>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1">
          <div className={META}>
            <dt className="inline">Účet: </dt>
            <dd className="inline">{proposal.accountExternalId}</dd>
          </div>
          <div className={META}>
            <dt className="inline">Autor: </dt>
            <dd className="inline">{`${labelFor(CREATOR_KIND_LABELS, proposal.createdByKind)} ${proposal.createdByPrincipal}`}</dd>
          </div>
          <div className={META}>
            <dt className="inline">Vytvořeno: </dt>
            <dd className="inline">{formatDateTime(proposal.createdAt)}</dd>
          </div>
          <div className={META}>
            <dt className="inline">Platnost do: </dt>
            <dd className="inline">{formatDateTime(proposal.expiresAt)}</dd>
          </div>
        </dl>
      </header>

      <LimitsEvaluationBadge limits={proposal.limits} />
      <ProposalDiff lines={proposal.diffLines} results={proposal.results} />
      <AgentReasoningBlock reasoning={proposal.reasoning} createdByKind={proposal.createdByKind} />
      {/* keyed by id + version so banner/dialog state never leaks to another proposal or a newer version */}
      <ProposalDecisionBar
        key={`${proposal.id}:${proposal.currentVersion}`}
        proposal={proposal}
        onRefresh={refresh}
        isRefreshing={query.isFetching}
      />
      <ProposalTimeline approvals={proposal.approvals} versions={proposal.versions} currentVersion={proposal.currentVersion} />
    </article>
  );
};

export default ProposalDetail;
