import React from 'react';
import { CampaignSummary } from '../../../hooks/useCampaignList';
import { useCampaignDetail } from '../../../hooks/useCampaignDetail';
import { CampaignAdSetRow } from './CampaignAdSetRow';

interface CampaignTableProps {
  campaigns: CampaignSummary[];
  isLoading: boolean;
  from: string;
  to: string;
  sortBy: keyof CampaignSummary;
  sortDesc: boolean;
  onSort: (col: keyof CampaignSummary) => void;
}

const formatCurrency = (v: number) =>
  new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(v);

const formatNumber = (v: number) =>
  new Intl.NumberFormat('cs-CZ').format(v);

const SortableHeader: React.FC<{
  col: keyof CampaignSummary;
  label: string;
  sortBy: keyof CampaignSummary;
  sortDesc: boolean;
  onSort: (col: keyof CampaignSummary) => void;
  align?: 'left' | 'right';
}> = ({ col, label, sortBy, sortDesc, onSort, align = 'left' }) => (
  <th
    className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-50 select-none text-${align}`}
    onClick={() => onSort(col)}
  >
    {label} {sortBy === col ? (sortDesc ? '↓' : '↑') : ''}
  </th>
);

const PlatformBadge: React.FC<{ platform: string }> = ({ platform }) => (
  <span
    className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${
      platform === 'Meta'
        ? 'bg-blue-100 text-blue-700'
        : 'bg-red-100 text-red-700'
    }`}
  >
    {platform}
  </span>
);

export const CampaignTable: React.FC<CampaignTableProps> = ({
  campaigns,
  isLoading,
  from,
  to,
  sortBy,
  sortDesc,
  onSort,
}) => {
  const [expandedIds, setExpandedIds] = React.useState<Set<string>>(new Set());
  const { details, loadingIds, fetchDetail } = useCampaignDetail();

  const toggleExpanded = (id: string) => {
    const next = new Set(expandedIds);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
      fetchDetail(id, from, to);
    }
    setExpandedIds(next);
  };

  if (isLoading) {
    return <div className="h-48 bg-gray-100 rounded animate-pulse" />;
  }

  return (
    <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
      <table className="w-full text-sm">
        <thead className="bg-gray-50 border-b border-gray-200">
          <tr>
            <SortableHeader col="name" label="Campaign" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} />
            <SortableHeader col="platform" label="Platform" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} />
            <SortableHeader col="status" label="Status" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} />
            <SortableHeader col="spend" label="Spend" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} align="right" />
            <SortableHeader col="impressions" label="Impressions" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} align="right" />
            <SortableHeader col="clicks" label="Clicks" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} align="right" />
            <SortableHeader col="conversions" label="Conv." sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} align="right" />
            <SortableHeader col="roas" label="ROAS" sortBy={sortBy} sortDesc={sortDesc} onSort={onSort} align="right" />
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {campaigns.map(c => (
            <React.Fragment key={c.id}>
              <tr
                className="hover:bg-gray-50 cursor-pointer"
                onClick={() => toggleExpanded(c.id)}
              >
                <td className="px-4 py-3 font-medium text-gray-900">
                  {expandedIds.has(c.id) ? '▾' : '▸'} {c.name}
                </td>
                <td className="px-4 py-3">
                  <PlatformBadge platform={c.platform} />
                </td>
                <td className="px-4 py-3 text-gray-500">{c.status}</td>
                <td className="px-4 py-3 text-right">{formatCurrency(c.spend)}</td>
                <td className="px-4 py-3 text-right">{formatNumber(c.impressions)}</td>
                <td className="px-4 py-3 text-right">{formatNumber(c.clicks)}</td>
                <td className="px-4 py-3 text-right">{c.conversions}</td>
                <td className="px-4 py-3 text-right">{c.roas.toFixed(2)}×</td>
              </tr>
              {expandedIds.has(c.id) && (
                loadingIds.has(c.id)
                  ? (
                    <tr>
                      <td colSpan={8} className="px-4 py-2 text-sm text-gray-400">Loading...</td>
                    </tr>
                  )
                  : details[c.id]?.adSets.map(adSet => (
                    <CampaignAdSetRow key={adSet.id} adSet={adSet} />
                  ))
              )}
            </React.Fragment>
          ))}
          {campaigns.length === 0 && (
            <tr>
              <td colSpan={8} className="px-4 py-8 text-center text-gray-400">
                No campaigns found for the selected period.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};
