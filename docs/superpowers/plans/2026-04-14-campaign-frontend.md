# Campaign Frontend Dashboard — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `/campaigns` page in React — 4 summary cards, spend-over-time line chart, filterable/sortable campaign table with expandable ad-set drill-down, and a sidebar "Marketing" navigation section.

**Architecture:** Follows existing page patterns (see `JournalList.tsx`, `Sidebar.tsx`). API hooks use absolute URLs via `getAuthenticatedApiClient()` with `(apiClient as any).baseUrl` prefix — this is mandatory per CLAUDE.md. Chart uses the same chart library already used on other pages. Layout follows `docs/design/layout_definition.md`.

**Tech Stack:** React 18, TypeScript, generated API client, Recharts (or whichever chart library the existing pages use), Tailwind CSS (or existing CSS approach)

**Prerequisite:** `campaign-query-api` plan must be completed and TypeScript client regenerated (types `CampaignDashboardDto`, `CampaignSummaryDto`, `CampaignDetailDto` must exist in generated client).

---

## File Map

**Create:**
- `frontend/src/components/pages/Campaigns/CampaignsPage.tsx`
- `frontend/src/components/pages/Campaigns/CampaignSummaryCard.tsx`
- `frontend/src/components/pages/Campaigns/CampaignSpendChart.tsx`
- `frontend/src/components/pages/Campaigns/CampaignTable.tsx`
- `frontend/src/components/pages/Campaigns/CampaignAdSetRow.tsx`
- `frontend/src/hooks/useCampaignDashboard.ts`
- `frontend/src/hooks/useCampaignList.ts`
- `frontend/src/hooks/useCampaignDetail.ts`

**Modify:**
- `frontend/src/components/Layout/Sidebar.tsx` — add Marketing section
- `frontend/src/App.tsx` (or router file) — add `/campaigns` route

---

### Task 1: API Hooks

**Files:**
- Create: `frontend/src/hooks/useCampaignDashboard.ts`
- Create: `frontend/src/hooks/useCampaignList.ts`
- Create: `frontend/src/hooks/useCampaignDetail.ts`

**Important:** All fetch calls MUST use absolute URLs. Pattern:
```typescript
const relativeUrl = `/api/campaigns/dashboard?from=...`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
```

- [ ] **Step 1: Create `useCampaignDashboard.ts`**

```typescript
import { useState, useEffect, useCallback } from 'react';
import { getAuthenticatedApiClient } from '../api/apiClient';

export interface DailySpend {
  date: string;
  metaSpend: number;
  googleSpend: number;
}

export interface CampaignDashboard {
  totalSpend: number;
  totalConversions: number;
  avgRoas: number;
  avgCpc: number;
  spendOverTime: DailySpend[];
}

export type AdPlatformFilter = 'Meta' | 'Google' | undefined;

export function useCampaignDashboard(
  from: string,
  to: string,
  platform: AdPlatformFilter
) {
  const [data, setData] = useState<CampaignDashboard | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchDashboard = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const params = new URLSearchParams({ from, to });
      if (platform) params.append('platform', platform);

      const relativeUrl = `/api/campaigns/dashboard?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const json = await response.json();
      setData(json);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard');
    } finally {
      setIsLoading(false);
    }
  }, [from, to, platform]);

  useEffect(() => {
    fetchDashboard();
  }, [fetchDashboard]);

  return { data, isLoading, error, refetch: fetchDashboard };
}
```

- [ ] **Step 2: Create `useCampaignList.ts`**

```typescript
import { useState, useEffect, useCallback } from 'react';
import { getAuthenticatedApiClient } from '../api/apiClient';
import { AdPlatformFilter } from './useCampaignDashboard';

export interface CampaignSummary {
  id: string;
  name: string;
  platform: 'Meta' | 'Google';
  status: string;
  spend: number;
  impressions: number;
  clicks: number;
  conversions: number;
  roas: number;
}

export function useCampaignList(
  from: string,
  to: string,
  platform: AdPlatformFilter
) {
  const [campaigns, setCampaigns] = useState<CampaignSummary[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchCampaigns = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const params = new URLSearchParams({ from, to });
      if (platform) params.append('platform', platform);

      const relativeUrl = `/api/campaigns?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const json = await response.json();
      setCampaigns(json);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load campaigns');
    } finally {
      setIsLoading(false);
    }
  }, [from, to, platform]);

  useEffect(() => {
    fetchCampaigns();
  }, [fetchCampaigns]);

  return { campaigns, isLoading, error, refetch: fetchCampaigns };
}
```

- [ ] **Step 3: Create `useCampaignDetail.ts`**

```typescript
import { useState, useCallback } from 'react';
import { getAuthenticatedApiClient } from '../api/apiClient';

export interface AdDetail {
  id: string;
  name: string;
  status: string;
  creativePreviewUrl?: string;
  spend: number;
  impressions: number;
  clicks: number;
  conversions: number;
}

export interface AdSetDetail {
  id: string;
  name: string;
  status: string;
  ads: AdDetail[];
}

export interface CampaignDetail {
  id: string;
  name: string;
  platform: 'Meta' | 'Google';
  status: string;
  adSets: AdSetDetail[];
}

export function useCampaignDetail() {
  const [details, setDetails] = useState<Record<string, CampaignDetail>>({});
  const [loadingIds, setLoadingIds] = useState<Set<string>>(new Set());

  const fetchDetail = useCallback(async (campaignId: string, from: string, to: string) => {
    if (details[campaignId]) return; // already loaded

    setLoadingIds(prev => new Set(prev).add(campaignId));

    try {
      const apiClient = await getAuthenticatedApiClient();
      const params = new URLSearchParams({ from, to });
      const relativeUrl = `/api/campaigns/${campaignId}?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });

      if (!response.ok) return;

      const json: CampaignDetail = await response.json();
      setDetails(prev => ({ ...prev, [campaignId]: json }));
    } finally {
      setLoadingIds(prev => {
        const next = new Set(prev);
        next.delete(campaignId);
        return next;
      });
    }
  }, [details]);

  return { details, loadingIds, fetchDetail };
}
```

- [ ] **Step 4: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/hooks/useCampaignDashboard.ts
git add frontend/src/hooks/useCampaignList.ts
git add frontend/src/hooks/useCampaignDetail.ts
git commit -m "feat(campaigns): add API hooks for dashboard, campaign list, and campaign detail"
```

---

### Task 2: Summary Cards Component

**Files:**
- Create: `frontend/src/components/pages/Campaigns/CampaignSummaryCard.tsx`

- [ ] **Step 1: Create `CampaignSummaryCard.tsx`**

```tsx
import React from 'react';

interface CampaignSummaryCardProps {
  title: string;
  value: string;
  isLoading: boolean;
}

export const CampaignSummaryCard: React.FC<CampaignSummaryCardProps> = ({
  title,
  value,
  isLoading,
}) => {
  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 flex flex-col gap-1">
      <span className="text-sm text-gray-500 font-medium">{title}</span>
      {isLoading ? (
        <div className="h-8 bg-gray-100 rounded animate-pulse" />
      ) : (
        <span className="text-2xl font-bold text-gray-900">{value}</span>
      )}
    </div>
  );
};
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/pages/Campaigns/CampaignSummaryCard.tsx
git commit -m "feat(campaigns): add CampaignSummaryCard component"
```

---

### Task 3: Spend Chart Component

**Files:**
- Create: `frontend/src/components/pages/Campaigns/CampaignSpendChart.tsx`

Before implementing, check which chart library is used on other pages. Look for imports like `recharts`, `chart.js`, or similar in existing page files (e.g., `frontend/src/components/pages/Analytics/`).

- [ ] **Step 1: Find the chart library used in this codebase**

```bash
grep -r "from 'recharts\|from 'chart.js\|from '@tremor" frontend/src --include="*.tsx" -l | head -5
```

Note the library name for use in the next step.

- [ ] **Step 2: Create `CampaignSpendChart.tsx`** (using Recharts — adjust imports if a different library is used)

```tsx
import React from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from 'recharts';
import { DailySpend } from '../../../hooks/useCampaignDashboard';

interface CampaignSpendChartProps {
  data: DailySpend[];
  isLoading: boolean;
}

const formatCurrency = (value: number) =>
  new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(value);

export const CampaignSpendChart: React.FC<CampaignSpendChartProps> = ({
  data,
  isLoading,
}) => {
  if (isLoading) {
    return <div className="h-64 bg-gray-100 rounded animate-pulse" />;
  }

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4">
      <h3 className="text-sm font-medium text-gray-700 mb-4">Spend Over Time</h3>
      <ResponsiveContainer width="100%" height={240}>
        <LineChart data={data} margin={{ top: 4, right: 16, left: 0, bottom: 4 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis
            dataKey="date"
            tick={{ fontSize: 11 }}
            tickFormatter={(d: string) => d.slice(5)} // "MM-DD"
          />
          <YAxis tick={{ fontSize: 11 }} tickFormatter={formatCurrency} width={80} />
          <Tooltip formatter={(value: number) => formatCurrency(value)} />
          <Legend />
          <Line
            type="monotone"
            dataKey="metaSpend"
            name="Meta"
            stroke="#1877f2"
            strokeWidth={2}
            dot={false}
          />
          <Line
            type="monotone"
            dataKey="googleSpend"
            name="Google"
            stroke="#ea4335"
            strokeWidth={2}
            dot={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
};
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/Campaigns/CampaignSpendChart.tsx
git commit -m "feat(campaigns): add CampaignSpendChart line chart component"
```

---

### Task 4: Campaign Table with Drill-down

**Files:**
- Create: `frontend/src/components/pages/Campaigns/CampaignAdSetRow.tsx`
- Create: `frontend/src/components/pages/Campaigns/CampaignTable.tsx`

- [ ] **Step 1: Create `CampaignAdSetRow.tsx`**

```tsx
import React from 'react';
import { AdSetDetail } from '../../../hooks/useCampaignDetail';

interface CampaignAdSetRowProps {
  adSet: AdSetDetail;
}

const formatCurrency = (v: number) =>
  new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(v);

const formatNumber = (v: number) =>
  new Intl.NumberFormat('cs-CZ').format(v);

export const CampaignAdSetRow: React.FC<CampaignAdSetRowProps> = ({ adSet }) => {
  const [expanded, setExpanded] = React.useState(false);

  return (
    <>
      <tr
        className="bg-gray-50 cursor-pointer hover:bg-gray-100"
        onClick={() => setExpanded(e => !e)}
      >
        <td className="px-4 py-2 pl-8 text-sm font-medium text-gray-700" colSpan={2}>
          {expanded ? '▾' : '▸'} {adSet.name}
        </td>
        <td className="px-4 py-2 text-sm text-gray-500">{adSet.status}</td>
        <td className="px-4 py-2 text-sm text-gray-500" colSpan={5}>
          {adSet.ads.length} ads
        </td>
      </tr>
      {expanded &&
        adSet.ads.map(ad => (
          <tr key={ad.id} className="bg-gray-50/50">
            <td className="px-4 py-2 pl-12 text-sm text-gray-600" colSpan={2}>
              {ad.creativePreviewUrl ? (
                <a
                  href={ad.creativePreviewUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="underline"
                >
                  {ad.name}
                </a>
              ) : (
                ad.name
              )}
            </td>
            <td className="px-4 py-2 text-xs text-gray-400">{ad.status}</td>
            <td className="px-4 py-2 text-sm text-right">{formatCurrency(ad.spend)}</td>
            <td className="px-4 py-2 text-sm text-right">{formatNumber(ad.impressions)}</td>
            <td className="px-4 py-2 text-sm text-right">{formatNumber(ad.clicks)}</td>
            <td className="px-4 py-2 text-sm text-right">{ad.conversions}</td>
            <td className="px-4 py-2 text-sm text-right">—</td>
          </tr>
        ))}
    </>
  );
};
```

- [ ] **Step 2: Create `CampaignTable.tsx`**

```tsx
import React from 'react';
import { CampaignSummary } from '../../../hooks/useCampaignList';
import { CampaignDetail, useCampaignDetail } from '../../../hooks/useCampaignDetail';
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
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/Campaigns/CampaignAdSetRow.tsx
git add frontend/src/components/pages/Campaigns/CampaignTable.tsx
git commit -m "feat(campaigns): add CampaignTable with sortable columns and expandable ad-set drill-down"
```

---

### Task 5: Main Campaigns Page

**Files:**
- Create: `frontend/src/components/pages/Campaigns/CampaignsPage.tsx`

- [ ] **Step 1: Create `CampaignsPage.tsx`**

```tsx
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
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/Campaigns/CampaignsPage.tsx
git commit -m "feat(campaigns): add CampaignsPage with filter bar, summary cards, chart, and campaign table"
```

---

### Task 6: Route and Navigation

**Files:**
- Modify: `frontend/src/App.tsx` (or router file)
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Add `/campaigns` route**

Find where existing routes are defined. Look for a pattern like:
```tsx
<Route path="/journal" element={<JournalList />} />
```

Add next to it:
```tsx
<Route path="/campaigns" element={<CampaignsPage />} />
```

Add the import at the top:
```tsx
import { CampaignsPage } from './components/pages/Campaigns/CampaignsPage';
```

- [ ] **Step 2: Add Marketing section to Sidebar**

In `frontend/src/components/Layout/Sidebar.tsx`, find the `navigationSections` array. Add a new section. Look for an appropriate icon import — find the list of imported icons at the top of the file (e.g., from `lucide-react`) and pick `TrendingUp` or `BarChart2`.

Add to the icons import:
```tsx
import { ..., TrendingUp } from 'lucide-react';
```

Add the section to `navigationSections` (place it after the existing sections, before the end of the array):
```tsx
{
  id: 'marketing',
  name: 'Marketing',
  icon: TrendingUp,
  type: 'section' as const,
  items: [
    { id: 'campaigns', name: 'Campaigns', href: '/campaigns' },
  ],
},
```

- [ ] **Step 3: Start dev server and verify page loads**

```bash
cd frontend && npm start
```

Navigate to `http://localhost:3000/campaigns`. Expected: Page renders with filter bar, 4 summary cards showing loading state, then data (or empty state if no sync has run yet).

- [ ] **Step 4: Lint check**

```bash
cd frontend && npm run lint
```

Expected: No lint errors.

- [ ] **Step 5: Build check**

```bash
cd frontend && npm run build
```

Expected: Build succeeded with no errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/App.tsx
git add frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat(campaigns): add /campaigns route and Marketing navigation section in sidebar"
```
