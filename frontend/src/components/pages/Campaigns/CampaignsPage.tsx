import React, { useState, useMemo } from 'react';
import { CampaignSummaryCard } from './CampaignSummaryCard';
import { CampaignSpendChart } from './CampaignSpendChart';
import { CampaignTable } from './CampaignTable';
import { useCampaignDashboard, AdPlatformFilter } from '../../../hooks/useCampaignDashboard';
import { useCampaignList, CampaignSummary } from '../../../hooks/useCampaignList';

const today = new Date();
const defaultTo = today.toISOString().slice(0, 10);
const defaultFrom = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

const formatCurrency = (v: number) =>
  new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(v);

export const CampaignsPage: React.FC = () => {
  const [from, setFrom] = useState(defaultFrom);
  const [to, setTo] = useState(defaultTo);
  const [platformFilter, setPlatformFilter] = useState<AdPlatformFilter>(undefined);
  const [fromInput, setFromInput] = useState(defaultFrom);
  const [toInput, setToInput] = useState(defaultTo);
  const [sortBy, setSortBy] = useState<keyof CampaignSummary>('spend');
  const [sortDesc, setSortDesc] = useState(true);

  const dashboard = useCampaignDashboard(from, to, platformFilter);
  const campaignList = useCampaignList(from, to, platformFilter);

  const sortedCampaigns = useMemo(() => {
    return [...campaignList.campaigns].sort((a, b) => {
      const aVal = a[sortBy];
      const bVal = b[sortBy];
      if (typeof aVal === 'number' && typeof bVal === 'number') {
        return sortDesc ? bVal - aVal : aVal - bVal;
      }
      return sortDesc
        ? String(bVal).localeCompare(String(aVal))
        : String(aVal).localeCompare(String(bVal));
    });
  }, [campaignList.campaigns, sortBy, sortDesc]);

  const handleSort = (col: keyof CampaignSummary) => {
    if (sortBy === col) {
      setSortDesc(d => !d);
    } else {
      setSortBy(col);
      setSortDesc(true);
    }
  };

  const handleApplyFilters = () => {
    setFrom(fromInput);
    setTo(toInput);
  };

  const handleClearFilters = () => {
    setFromInput(defaultFrom);
    setToInput(defaultTo);
    setFrom(defaultFrom);
    setTo(defaultTo);
    setPlatformFilter(undefined);
  };

  return (
    <div className="flex flex-col gap-6 p-6">
      {/* Page Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-gray-900">Campaigns</h1>
      </div>

      {/* Filter Bar */}
      <div className="flex flex-wrap items-center gap-3 bg-white rounded-lg border border-gray-200 p-4">
        <div className="flex items-center gap-2">
          <label className="text-sm text-gray-600">From</label>
          <input
            type="date"
            value={fromInput}
            onChange={e => setFromInput(e.target.value)}
            className="border border-gray-300 rounded px-2 py-1 text-sm"
          />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-sm text-gray-600">To</label>
          <input
            type="date"
            value={toInput}
            onChange={e => setToInput(e.target.value)}
            className="border border-gray-300 rounded px-2 py-1 text-sm"
          />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-sm text-gray-600">Platform</label>
          <select
            value={platformFilter ?? ''}
            onChange={e => setPlatformFilter((e.target.value as AdPlatformFilter) || undefined)}
            className="border border-gray-300 rounded px-2 py-1 text-sm"
          >
            <option value="">All</option>
            <option value="Meta">Meta</option>
            <option value="Google">Google</option>
          </select>
        </div>
        <button
          onClick={handleApplyFilters}
          className="px-3 py-1.5 bg-blue-600 text-white text-sm rounded hover:bg-blue-700"
        >
          Apply
        </button>
        <button
          onClick={handleClearFilters}
          className="px-3 py-1.5 bg-white border border-gray-300 text-sm rounded hover:bg-gray-50"
        >
          Clear
        </button>
      </div>

      {/* Summary Cards */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <CampaignSummaryCard
          title="Total Spend"
          value={dashboard.data ? formatCurrency(dashboard.data.totalSpend) : '—'}
          isLoading={dashboard.isLoading}
        />
        <CampaignSummaryCard
          title="Total Conversions"
          value={dashboard.data ? String(dashboard.data.totalConversions) : '—'}
          isLoading={dashboard.isLoading}
        />
        <CampaignSummaryCard
          title="Avg ROAS"
          value={dashboard.data ? `${dashboard.data.avgRoas.toFixed(2)}×` : '—'}
          isLoading={dashboard.isLoading}
        />
        <CampaignSummaryCard
          title="Avg CPC"
          value={dashboard.data ? formatCurrency(dashboard.data.avgCpc) : '—'}
          isLoading={dashboard.isLoading}
        />
      </div>

      {/* Spend Chart */}
      <CampaignSpendChart
        data={dashboard.data?.spendOverTime ?? []}
        isLoading={dashboard.isLoading}
      />

      {/* Campaign Table */}
      <CampaignTable
        campaigns={sortedCampaigns}
        isLoading={campaignList.isLoading}
        from={from}
        to={to}
        sortBy={sortBy}
        sortDesc={sortDesc}
        onSort={handleSort}
      />
    </div>
  );
};

export default CampaignsPage;
