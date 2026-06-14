import React, { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import { usePermissionsContext } from '../auth/PermissionsContext';
import { useKbFeedbackAdapter } from '../components/feedback/adapters/useKbFeedbackAdapter';
import { useLeafletFeedbackAdapter } from '../components/feedback/adapters/useLeafletFeedbackAdapter';
import { useArticleFeedbackAdapter } from '../components/feedback/adapters/useArticleFeedbackAdapter';
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

type FeatureTab = 'kb' | 'leaflet' | 'article';

const TAB_LABELS: Record<FeatureTab, string> = {
  kb: 'Poradenství (KB)',
  leaflet: 'Letáky',
  article: 'Články',
};

const ITEM_LABELS: Record<FeatureTab, string> = {
  kb: 'dotazů',
  leaflet: 'generování',
  article: 'článků',
};

const PRIMARY_LABELS: Record<FeatureTab, string> = {
  kb: 'Dotaz',
  leaflet: 'Téma',
  article: 'Téma článku',
};

const SECONDARY_LABELS: Record<FeatureTab, string> = {
  kb: 'Odpověď',
  leaflet: 'Výstup',
  article: 'Téma',
};

const MarketingFeedbackPage: React.FC = () => {
  const { hasPermission } = usePermissionsContext();
  const hasKb = hasPermission('customer.knowledge_base.write');
  const hasGenAi = hasPermission('marketing.article.write') || hasPermission('marketing.leaflet.write');

  const [activeTab, setActiveTab] = useState<FeatureTab>('kb');
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [kbParams, setKbParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [leafletParams, setLeafletParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [articleParams, setArticleParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);

  useScreenView('Marketing', 'MarketingFeedback');

  const kb = useKbFeedbackAdapter(kbParams);
  const leaflet = useLeafletFeedbackAdapter(leafletParams);
  const article = useArticleFeedbackAdapter(articleParams);

  if (!hasKb && !hasGenAi) {
    return <div className="p-6 text-sm text-gray-500">Přístup odepřen.</div>;
  }

  const activeData = { kb, leaflet, article }[activeTab];
  const activeParams = { kb: kbParams, leaflet: leafletParams, article: articleParams }[activeTab];
  const setActiveParams = {
    kb: setKbParams,
    leaflet: setLeafletParams,
    article: setArticleParams,
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
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {/* Tab bar */}
        <div className="flex gap-1 border-b border-gray-200">
          {(Object.keys(TAB_LABELS) as FeatureTab[]).map((tab) => (
            <button
              key={tab}
              onClick={() => handleTabChange(tab)}
              className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab
                  ? 'border-blue-600 text-blue-700'
                  : 'border-transparent text-gray-600 hover:text-gray-900'
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
          <div className="flex items-center justify-center h-32 text-sm text-red-600">
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
