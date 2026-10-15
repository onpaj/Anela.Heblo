import React, { useState } from 'react';
import ErrorState from '../../../common/ErrorState';
import { PAGE_CONTAINER_HEIGHT } from '../../../../constants/layout';
import { useScreenView } from '../../../../telemetry/useScreenView';
import {
  useAdAuditEventsQuery,
  useAdChangeEventsQuery,
  useAgentRunsQuery,
} from '../../../../api/hooks/useMarketingAdsActivity';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import type { ChangeEventFilter } from '../../../../api/hooks/marketingAds/keys';
import AdsTabs, { type AdsTab } from '../shared/AdsTabs';
import AdsLoading from '../shared/AdsLoading';
import MetabaseLink from '../shared/MetabaseLink';
import Pager from '../shared/Pager';
import AgentRunsTable from './AgentRunsTable';
import AuditLogTable from './AuditLogTable';
import ChangeEventFilters from './ChangeEventFilters';
import ChangeEventsTable from './ChangeEventsTable';

type ActivityTab = 'audit' | 'changes' | 'runs';

const TABS: ReadonlyArray<AdsTab<ActivityTab>> = [
  { id: 'audit', label: 'Auditní log' },
  { id: 'changes', label: 'Změny v účtech' },
  { id: 'runs', label: 'Běhy agentů' },
];

const PAGE_SIZE = 50;
const DEFAULT_FILTER: ChangeEventFilter = { platform: null, origin: 'OutOfBand', actorKind: null };

const MarketingAdsActivityPage: React.FC = () => {
  useScreenView('Marketing', 'MarketingAdsActivity');
  const [tab, setTab] = useState<ActivityTab>('audit');
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState<ChangeEventFilter>(DEFAULT_FILTER);

  const audit = useAdAuditEventsQuery({ page, pageSize: PAGE_SIZE }, tab === 'audit');
  const changes = useAdChangeEventsQuery({ ...filter, page, pageSize: PAGE_SIZE }, tab === 'changes');
  const runs = useAgentRunsQuery({ page, pageSize: PAGE_SIZE }, tab === 'runs');

  const activeQueries = { audit, changes, runs };
  const active = activeQueries[tab];
  const totalCount = active.data?.totalCount ?? 0;
  const isEmpty = active.data !== undefined && active.data.items.length === 0;

  const handleTabChange = (nextTab: ActivityTab) => {
    setTab(nextTab);
    setPage(1);
  };

  const handleFilterChange = (nextFilter: ChangeEventFilter) => {
    setFilter(nextFilter);
    setPage(1);
  };

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3 flex flex-wrap items-start justify-between gap-2">
        <div>
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Aktivita v reklamách</h1>
          <p className="mt-1 text-gray-600 dark:text-graphite-muted">
            Kdo co navrhl, schválil a provedl — včetně změn provedených mimo Heblo a běhů agentů.
          </p>
        </div>
        <MetabaseLink />
      </div>

      <div className="flex-shrink-0 mb-3">
        <AdsTabs tabs={TABS} active={tab} onChange={handleTabChange} />
      </div>

      {tab === 'changes' && (
        <div className="flex-shrink-0 mb-3 flex flex-wrap items-end gap-4">
          <ChangeEventFilters filter={filter} onChange={handleFilterChange} />
          <p className="text-xs text-gray-500 dark:text-graphite-muted pb-1">Posledních 30 dní.</p>
        </div>
      )}

      <div className="flex-1 min-h-0 flex flex-col bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-md">
        <div className="flex-1 overflow-auto">
          {active.isLoading && <AdsLoading />}
          {active.error && <ErrorState message={describeAdsError(active.error)} className="py-8" />}
          {isEmpty && <p className="p-4 text-sm text-gray-500 dark:text-graphite-muted">Zatím žádné záznamy.</p>}
          {tab === 'audit' && audit.data && audit.data.items.length > 0 && <AuditLogTable items={audit.data.items} />}
          {tab === 'changes' && changes.data && changes.data.items.length > 0 && <ChangeEventsTable items={changes.data.items} />}
          {tab === 'runs' && runs.data && runs.data.items.length > 0 && <AgentRunsTable items={runs.data.items} />}
        </div>
        <Pager page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
      </div>
    </div>
  );
};

export default MarketingAdsActivityPage;
