import React, { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import { usePermissionsContext } from '../auth/PermissionsContext';
import { useKbFeedbackAdapter } from '../components/feedback/adapters/useKbFeedbackAdapter';
import { useLeafletFeedbackAdapter } from '../components/feedback/adapters/useLeafletFeedbackAdapter';
import { useArticleFeedbackAdapter } from '../components/feedback/adapters/useArticleFeedbackAdapter';
import { useSmartsuppFeedbackAdapter } from '../components/feedback/adapters/useSmartsuppFeedbackAdapter';
import GenericFeedbackStatsBar from '../components/feedback/GenericFeedbackStatsBar';
import GenericFeedbackFilters from '../components/feedback/GenericFeedbackFilters';
import GenericFeedbackTable from '../components/feedback/GenericFeedbackTable';
import GenericFeedbackDetailModal from '../components/feedback/GenericFeedbackDetailModal';
import {
  DEFAULT_FEEDBACK_PARAMS,
  SORT_COLUMNS,
  type FeedbackDetail,
  type GenericFeedbackParams,
} from '../components/feedback/types';
import { useScreenView } from '../telemetry/useScreenView';

type FeatureTab = 'kb' | 'leaflet' | 'article' | 'smartsupp';

const TAB_LABELS: Record<FeatureTab, string> = {
  kb: 'Poradenství (KB)',
  leaflet: 'Letáky',
  article: 'Články',
  smartsupp: 'Smartsupp',
};

const ITEM_LABELS: Record<FeatureTab, string> = {
  kb: 'dotazů',
  leaflet: 'generování',
  article: 'článků',
  smartsupp: 'návrhů',
};

const PRIMARY_LABELS: Record<FeatureTab, string> = {
  kb: 'Dotaz',
  leaflet: 'Téma',
  article: 'Téma článku',
  smartsupp: 'Téma / dotaz',
};

const SECONDARY_LABELS: Record<FeatureTab, string> = {
  kb: 'Odpověď',
  leaflet: 'Výstup',
  article: 'Téma',
  smartsupp: 'Návrh',
};

const MarketingFeedbackPage: React.FC = () => {
  const { hasPermission } = usePermissionsContext();
  const hasKb = hasPermission('customer.knowledge_base.write');
  const hasGenAi = hasPermission('marketing.article.write') || hasPermission('marketing.leaflet.write');
  const hasSmartsupp = hasPermission('customer.smartsupp.read');

  const [activeTab, setActiveTab] = useState<FeatureTab>('kb');
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [kbParams, setKbParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [leafletParams, setLeafletParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [articleParams, setArticleParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [smartsuppParams, setSmartsuppParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);

  useScreenView('Marketing', 'MarketingFeedback');

  const kb = useKbFeedbackAdapter(kbParams);
  const leaflet = useLeafletFeedbackAdapter(leafletParams);
  const article = useArticleFeedbackAdapter(articleParams);
  const smartsupp = useSmartsuppFeedbackAdapter(smartsuppParams);

  if (!hasKb && !hasGenAi && !hasSmartsupp) {
    return <div className="p-6 text-sm text-gray-500 dark:text-graphite-muted">Přístup odepřen.</div>;
  }

  // Smartsupp is customer-support (not marketing) — only surfaced when the user has that permission.
  const visibleTabs: FeatureTab[] = ['kb', 'leaflet', 'article', ...(hasSmartsupp ? ['smartsupp' as const] : [])];

  const activeData = { kb, leaflet, article, smartsupp }[activeTab];
  const activeParams = {
    kb: kbParams,
    leaflet: leafletParams,
    article: articleParams,
    smartsupp: smartsuppParams,
  }[activeTab];
  const setActiveParams = {
    kb: setKbParams,
    leaflet: setLeafletParams,
    article: setArticleParams,
    smartsupp: setSmartsuppParams,
  }[activeTab];

  const selectedRow: FeedbackDetail | undefined = activeData.rows.find(
    (r) => r.id === selectedRowId,
  );

  const handleTabChange = (tab: FeatureTab) => {
    setActiveTab(tab);
    setSelectedRowId(null);
  };

  const handleParamChange = (update: Partial<GenericFeedbackParams>) => {
    setActiveParams((prev) => ({ ...prev, ...update, pageNumber: 1 }));
    setSelectedRowId(null);
  };

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-4 border-b border-gray-200 dark:border-graphite-border flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600 dark:text-graphite-accent" />
        <h1 className="text-2xl font-semibold text-gray-900 dark:text-graphite-text">Feedback</h1>
      </div>

      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {/* Tab bar */}
        <div className="flex gap-1 border-b border-gray-200 dark:border-graphite-border">
          {visibleTabs.map((tab) => (
            <button
              key={tab}
              onClick={() => handleTabChange(tab)}
              className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab
                  ? 'border-blue-600 text-blue-700 dark:text-graphite-accent dark:border-graphite-accent'
                  : 'border-transparent text-gray-600 hover:text-gray-900 dark:text-graphite-muted'
              }`}
            >
              {TAB_LABELS[tab]}
            </button>
          ))}
        </div>

        <GenericFeedbackStatsBar
          stats={activeData.stats}
          isLoading={activeData.isLoading}
          itemLabel={ITEM_LABELS[activeTab]}
        />

        <GenericFeedbackFilters
          hasFeedback={activeParams.hasFeedback}
          sortBy={activeParams.sortBy}
          sortDescending={activeParams.sortDescending}
          pageSize={activeParams.pageSize}
          allowedSortColumns={[...SORT_COLUMNS]}
          onHasFeedbackChange={(v) => handleParamChange({ hasFeedback: v })}
          onSortByChange={(v) => handleParamChange({ sortBy: v })}
          onSortDescendingChange={(v) => handleParamChange({ sortDescending: v })}
          onPageSizeChange={(v) => handleParamChange({ pageSize: v })}
        />

        {activeData.isError && (
          <div className="flex items-center justify-center h-32 text-sm text-red-600 dark:text-red-400">
            Nepodařilo se načíst záznamy. Zkuste to znovu.
          </div>
        )}

        {!activeData.isError && (
          <GenericFeedbackTable
            rows={activeData.rows}
            isLoading={activeData.isLoading}
            totalCount={activeData.totalCount}
            pageNumber={activeData.pageNumber}
            pageSize={activeParams.pageSize}
            totalPages={activeData.totalPages}
            onPageChange={(page) =>
              setActiveParams((prev) => ({ ...prev, pageNumber: page }))
            }
            onRowClick={(id) =>
              setSelectedRowId((prev) => (prev === id ? null : id))
            }
            primaryLabel={PRIMARY_LABELS[activeTab]}
          />
        )}
      </div>

      {selectedRow && (
        <GenericFeedbackDetailModal
          detail={selectedRow}
          onClose={() => setSelectedRowId(null)}
          primaryLabel={PRIMARY_LABELS[activeTab]}
          secondaryLabel={SECONDARY_LABELS[activeTab]}
        />
      )}
    </div>
  );
};

export default MarketingFeedbackPage;
