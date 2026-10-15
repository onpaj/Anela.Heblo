import React, { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import ErrorState from '../../../common/ErrorState';
import { PAGE_CONTAINER_HEIGHT } from '../../../../constants/layout';
import { useScreenView } from '../../../../telemetry/useScreenView';
import { useAdProposalsQuery } from '../../../../api/hooks/useMarketingAdsProposals';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import { PROPOSAL_STATUS } from '../../../../api/hooks/marketingAds/types';
import AdsTabs, { type AdsTab } from '../shared/AdsTabs';
import AdsLoading from '../shared/AdsLoading';
import MetabaseLink from '../shared/MetabaseLink';
import Pager from '../shared/Pager';
import ProposalDetail from './ProposalDetail';
import ProposalList from './ProposalList';

type ProposalTab = 'pending' | 'history';

const TABS: ReadonlyArray<AdsTab<ProposalTab>> = [
  { id: 'pending', label: 'Ke schválení' },
  { id: 'history', label: 'Historie' },
];

const PAGE_SIZE = 25;
const PANEL = 'bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-md min-h-0';

const MarketingAdsProposalsPage: React.FC = () => {
  useScreenView('Marketing', 'MarketingAdsProposals');
  const [searchParams, setSearchParams] = useSearchParams();
  const [page, setPage] = useState(1);
  const tab: ProposalTab = searchParams.get('tab') === 'history' ? 'history' : 'pending';
  const selectedId = searchParams.get('proposal');

  const list = useAdProposalsQuery({
    status: tab === 'pending' ? PROPOSAL_STATUS.Pending : null,
    page,
    pageSize: PAGE_SIZE,
  });

  const updateParams = (changes: Record<string, string | null>) => {
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous);
      Object.entries(changes).forEach(([key, value]) => {
        if (value === null) {
          next.delete(key);
        } else {
          next.set(key, value);
        }
      });
      return next;
    });
  };

  const handleTabChange = (nextTab: ProposalTab) => {
    setPage(1);
    updateParams({ tab: nextTab === 'history' ? 'history' : null });
  };

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3 flex flex-wrap items-start justify-between gap-2">
        <div>
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Návrhy změn v reklamách</h1>
          <p className="mt-1 text-gray-600 dark:text-graphite-muted">
            Návrhy agentů a kolegů ke schválení. Schvalujete vždy konkrétní verzi návrhu.
          </p>
        </div>
        <MetabaseLink />
      </div>

      <div className="flex-shrink-0 mb-3">
        <AdsTabs tabs={TABS} active={tab} onChange={handleTabChange} />
      </div>

      <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-5 gap-4 overflow-auto lg:overflow-hidden">
        <section aria-label="Seznam návrhů" className={`lg:col-span-2 flex flex-col ${PANEL}`}>
          <div className="flex-1 overflow-auto">
            {list.isLoading && <AdsLoading />}
            {list.error && <ErrorState message={describeAdsError(list.error)} className="py-8" />}
            {list.data && (
              <ProposalList
                items={list.data.items}
                selectedId={selectedId}
                emptyText={tab === 'pending' ? 'Nic nečeká na schválení.' : 'Zatím žádné návrhy.'}
                onSelect={(id) => updateParams({ proposal: id })}
              />
            )}
          </div>
          {list.data && (
            <Pager page={page} pageSize={PAGE_SIZE} totalCount={list.data.totalCount} onPageChange={setPage} />
          )}
        </section>

        <section aria-label="Detail návrhu" className={`lg:col-span-3 overflow-auto ${PANEL}`}>
          {selectedId ? (
            <ProposalDetail proposalId={selectedId} />
          ) : (
            <p className="p-6 text-sm text-gray-500 dark:text-graphite-muted">Vyberte návrh ze seznamu.</p>
          )}
        </section>
      </div>
    </div>
  );
};

export default MarketingAdsProposalsPage;
