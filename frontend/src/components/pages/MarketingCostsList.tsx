import React, { useState } from "react";
import { ChevronUp, ChevronDown, AlertCircle, Loader2 } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import {
  useMarketingCostsQuery,
  GetMarketingCostsRequest,
  MarketingCostListItemDto,
} from "../../api/hooks/useMarketingCosts";
import MarketingCostDetail from "./MarketingCostDetail";
import Pagination from "../common/Pagination";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const platformBadge: Record<string, string> = {
  GoogleAds: "bg-blue-100 text-blue-800",
  MetaAds: "bg-purple-100 text-purple-800",
};

const MarketingCostsList: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  // Filter input state
  const [platformInput, setPlatformInput] = useState(searchParams.get("platform") || "");
  const [dateFromInput, setDateFromInput] = useState(searchParams.get("dateFrom") || "");
  const [dateToInput, setDateToInput] = useState(searchParams.get("dateTo") || "");
  const [syncedInput, setSyncedInput] = useState(searchParams.get("isSynced") || "");

  // Applied filter state
  const [platformFilter, setPlatformFilter] = useState(searchParams.get("platform") || "");
  const [dateFromFilter, setDateFromFilter] = useState(searchParams.get("dateFrom") || "");
  const [dateToFilter, setDateToFilter] = useState(searchParams.get("dateTo") || "");
  const [syncedFilter, setSyncedFilter] = useState(searchParams.get("isSynced") || "");

  // Pagination
  const [pageNumber, setPageNumber] = useState(Number(searchParams.get("page")) || 1);
  const [pageSize, setPageSize] = useState(Number(searchParams.get("pageSize")) || 20);

  // Sorting
  const [sortBy, setSortBy] = useState(searchParams.get("sortBy") || "transactionDate");
  const [sortDescending, setSortDescending] = useState(
    searchParams.get("sortDesc") !== null
      ? searchParams.get("sortDesc") === "true"
      : true
  );

  // Detail modal
  const [selectedItem, setSelectedItem] = useState<MarketingCostListItemDto | null>(null);
  const [isDetailOpen, setIsDetailOpen] = useState(false);

  const request: GetMarketingCostsRequest = {
    platform: platformFilter || undefined,
    dateFrom: dateFromFilter || undefined,
    dateTo: dateToFilter || undefined,
    isSynced: syncedFilter === "" ? null : syncedFilter === "true",
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
  };

  const { data, isLoading, error } = useMarketingCostsQuery(request);

  const updateSearchParams = (overrides: {
    platform?: string;
    dateFrom?: string;
    dateTo?: string;
    isSynced?: string;
    page?: number;
    pageSize?: number;
    sortBy?: string;
    sortDesc?: boolean;
  }) => {
    const params = new URLSearchParams();
    const p = overrides.platform ?? platformFilter;
    const df = overrides.dateFrom ?? dateFromFilter;
    const dt = overrides.dateTo ?? dateToFilter;
    const sy = overrides.isSynced ?? syncedFilter;
    const pg = overrides.page ?? pageNumber;
    const ps = overrides.pageSize ?? pageSize;
    const sb = overrides.sortBy ?? sortBy;
    const sd = overrides.sortDesc ?? sortDescending;

    if (p) params.set("platform", p);
    if (df) params.set("dateFrom", df);
    if (dt) params.set("dateTo", dt);
    if (sy) params.set("isSynced", sy);
    params.set("page", pg.toString());
    params.set("pageSize", ps.toString());
    params.set("sortBy", sb);
    params.set("sortDesc", sd.toString());
    setSearchParams(params);
  };

  const applyFilters = () => {
    setPlatformFilter(platformInput);
    setDateFromFilter(dateFromInput);
    setDateToFilter(dateToInput);
    setSyncedFilter(syncedInput);
    setPageNumber(1);
    updateSearchParams({
      platform: platformInput,
      dateFrom: dateFromInput,
      dateTo: dateToInput,
      isSynced: syncedInput,
      page: 1,
    });
  };

  const handleSort = (column: string) => {
    const newDesc = sortBy === column ? !sortDescending : true;
    const newSortBy = column;
    setSortBy(newSortBy);
    setSortDescending(newDesc);
    setPageNumber(1);
    updateSearchParams({ sortBy: newSortBy, sortDesc: newDesc, page: 1 });
  };

  const handleRowClick = (item: MarketingCostListItemDto) => {
    setSelectedItem(item);
    setIsDetailOpen(true);
  };

  const handlePageChange = (page: number) => {
    setPageNumber(page);
    updateSearchParams({ page });
  };

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setPageNumber(1);
    updateSearchParams({ pageSize: size, page: 1 });
  };

  const SortableHeader = ({ column, label }: { column: string; label: string }) => (
    <th
      className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
      onClick={() => handleSort(column)}
    >
      <div className="flex items-center gap-1">
        {label}
        {sortBy === column && (
          sortDescending ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />
        )}
      </div>
    </th>
  );

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <AlertCircle className="w-5 h-5 text-red-500 mr-2" />
        <span className="text-red-600">Chyba při načítání dat</span>
      </div>
    );
  }

  return (
    <div className="flex flex-col" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-200">
        <h1 className="text-lg font-semibold text-gray-900">Marketingové náklady</h1>
      </div>

      {/* Filters */}
      <div className="px-4 py-3 border-b border-gray-200 bg-gray-50 flex gap-3 items-end flex-wrap">
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Platforma</label>
          <select
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={platformInput}
            onChange={(e) => setPlatformInput(e.target.value)}
          >
            <option value="">Všechny</option>
            <option value="GoogleAds">Google Ads</option>
            <option value="MetaAds">Meta Ads</option>
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Od</label>
          <input
            type="date"
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={dateFromInput}
            onChange={(e) => setDateFromInput(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Do</label>
          <input
            type="date"
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={dateToInput}
            onChange={(e) => setDateToInput(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Sync</label>
          <select
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={syncedInput}
            onChange={(e) => setSyncedInput(e.target.value)}
          >
            <option value="">Všechny</option>
            <option value="true">Synced</option>
            <option value="false">Not synced</option>
          </select>
        </div>
        <button
          className="px-3 py-1.5 bg-blue-600 text-white rounded text-sm hover:bg-blue-700"
          onClick={applyFilters}
        >
          Filtrovat
        </button>
      </div>

      {/* Table */}
      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
          </div>
        ) : (
          <table className="w-full">
            <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Platforma
                </th>
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Transaction ID
                </th>
                <SortableHeader column="amount" label="Částka" />
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Měna
                </th>
                <SortableHeader column="transactionDate" label="Datum transakce" />
                <SortableHeader column="importedAt" label="Importováno" />
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Sync
                </th>
              </tr>
            </thead>
            <tbody>
              {data?.items.map((item) => (
                <tr
                  key={item.id}
                  className="border-b border-gray-100 hover:bg-gray-50 cursor-pointer"
                  onClick={() => handleRowClick(item)}
                >
                  <td className="px-3 py-2">
                    <span
                      className={`px-2 py-0.5 rounded text-xs font-medium ${platformBadge[item.platform] || "bg-gray-100 text-gray-800"}`}
                    >
                      {item.platform}
                    </span>
                  </td>
                  <td className="px-3 py-2 font-mono text-xs text-gray-700">
                    {item.transactionId}
                  </td>
                  <td className="px-3 py-2 font-medium">
                    {item.amount.toLocaleString("cs-CZ", { minimumFractionDigits: 2 })}
                  </td>
                  <td className="px-3 py-2 text-sm text-gray-600">
                    {item.currency || "—"}
                  </td>
                  <td className="px-3 py-2 text-sm">
                    {new Date(item.transactionDate).toLocaleDateString("cs-CZ")}
                  </td>
                  <td className="px-3 py-2 text-sm text-gray-500">
                    {new Date(item.importedAt).toLocaleString("cs-CZ")}
                  </td>
                  <td className="px-3 py-2">
                    {item.isSynced ? (
                      <span className="text-green-600">✓</span>
                    ) : (
                      <span className="text-red-600">✗</span>
                    )}
                  </td>
                </tr>
              ))}
              {data?.items.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-3 py-8 text-center text-gray-500">
                    Žádné záznamy
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination */}
      {data && (
        <div className="border-t border-gray-200">
          <Pagination
            totalCount={data.totalCount}
            pageNumber={data.pageNumber}
            pageSize={data.pageSize}
            totalPages={data.totalPages}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
            isFiltered={!!(platformFilter || dateFromFilter || dateToFilter || syncedFilter)}
          />
        </div>
      )}

      {/* Detail Modal */}
      <MarketingCostDetail
        item={selectedItem}
        isOpen={isDetailOpen}
        onClose={() => setIsDetailOpen(false)}
      />
    </div>
  );
};

export default MarketingCostsList;
