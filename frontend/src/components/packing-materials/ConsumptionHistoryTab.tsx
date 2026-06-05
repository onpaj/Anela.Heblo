import React, { useState } from 'react';
import { Search, X, ChevronDown, ChevronUp, RefreshCw, AlertCircle } from 'lucide-react';
import {
  useConsumptionHistory,
  usePackingMaterials,
  ConsumptionType,
  ConsumptionHistoryParams,
  ConsumptionHistoryItemDto,
} from '../../api/hooks/usePackingMaterials';
import Pagination from '../common/Pagination';

const CONSUMPTION_TYPE_OPTIONS: { value: ConsumptionType; label: string }[] = [
  { value: ConsumptionType.PerOrder, label: 'Na objednávku' },
  { value: ConsumptionType.PerProduct, label: 'Na produkt' },
  { value: ConsumptionType.PerDay, label: 'Na den' },
];

const DASH = '—';

const formatNumber = (value?: number): string =>
  value === undefined || value === null
    ? DASH
    : value.toLocaleString('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: 2 });

const formatDate = (value?: string): string => (value ? new Date(value).toLocaleDateString('cs-CZ') : DASH);

const ConsumptionHistoryTab: React.FC = () => {
  const { data: materialsData } = usePackingMaterials();
  const materials = materialsData?.materials ?? [];

  // Filter input state (what the user is editing)
  const [dateFromInput, setDateFromInput] = useState('');
  const [dateToInput, setDateToInput] = useState('');
  const [materialInput, setMaterialInput] = useState<string>('');
  const [typeInput, setTypeInput] = useState<string>('');
  const [productCodeInput, setProductCodeInput] = useState('');
  const [invoiceIdInput, setInvoiceIdInput] = useState('');

  // Applied filter state (sent to the API)
  const [appliedFilters, setAppliedFilters] = useState<ConsumptionHistoryParams>({});

  // Paging + sorting
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [sortDescending, setSortDescending] = useState(true);

  const { data, isLoading, error, refetch } = useConsumptionHistory({
    ...appliedFilters,
    pageNumber,
    pageSize,
    sortDescending,
  });

  const handleApplyFilters = () => {
    setAppliedFilters({
      dateFrom: dateFromInput || undefined,
      dateTo: dateToInput || undefined,
      packingMaterialId: materialInput ? Number(materialInput) : undefined,
      consumptionType: typeInput ? (Number(typeInput) as ConsumptionType) : undefined,
      productCode: productCodeInput || undefined,
      invoiceId: invoiceIdInput || undefined,
    });
    setPageNumber(1);
  };

  const handleClearFilters = () => {
    setDateFromInput('');
    setDateToInput('');
    setMaterialInput('');
    setTypeInput('');
    setProductCodeInput('');
    setInvoiceIdInput('');
    setAppliedFilters({});
    setPageNumber(1);
  };

  const handleSortByDate = () => {
    setSortDescending((prev) => !prev);
    setPageNumber(1);
  };

  const items: ConsumptionHistoryItemDto[] = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  if (error) {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-5 w-5 text-red-600" />
          <div>
            <h3 className="text-red-800 font-semibold">Chyba při načítání historie spotřeby</h3>
            <p className="text-red-600 text-sm mt-1">
              {error instanceof Error ? error.message : 'Neznámá chyba'}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 text-sm"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      {/* Filters */}
      <div className="bg-white rounded-lg shadow mb-4 p-3 space-y-3">
        <div className="flex flex-wrap gap-3 items-end">
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Datum od</label>
            <input
              type="date"
              value={dateFromInput}
              onChange={(e) => setDateFromInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Datum do</label>
            <input
              type="date"
              value={dateToInput}
              onChange={(e) => setDateToInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="w-56">
            <label className="block text-xs font-medium text-gray-700 mb-1">Materiál</label>
            <select
              value={materialInput}
              onChange={(e) => setMaterialInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="">Všechny</option>
              {materials.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.name}
                </option>
              ))}
            </select>
          </div>
          <div className="w-48">
            <label className="block text-xs font-medium text-gray-700 mb-1">Typ spotřeby</label>
            <select
              value={typeInput}
              onChange={(e) => setTypeInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="">Všechny</option>
              {CONSUMPTION_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Kód produktu</label>
            <input
              type="text"
              value={productCodeInput}
              onChange={(e) => setProductCodeInput(e.target.value)}
              placeholder="Kód produktu"
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Faktura</label>
            <input
              type="text"
              value={invoiceIdInput}
              onChange={(e) => setInvoiceIdInput(e.target.value)}
              placeholder="ID faktury"
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
        </div>
        <div className="flex justify-end gap-2">
          <button
            onClick={handleClearFilters}
            className="flex items-center gap-1 px-3 py-1.5 text-xs text-gray-600 hover:text-gray-800 hover:bg-gray-100 rounded-md border border-gray-300"
          >
            <X className="h-3 w-3" />
            Vymazat filtry
          </button>
          <button
            onClick={handleApplyFilters}
            className="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md"
          >
            <Search className="h-3 w-3" />
            Použít filtry
          </button>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden flex flex-col">
        {isLoading ? (
          <div className="flex items-center justify-center py-16">
            <RefreshCw className="h-6 w-6 animate-spin text-gray-400" />
            <span className="ml-2 text-gray-600 text-sm">Načítání dat...</span>
          </div>
        ) : items.length === 0 ? (
          <div className="text-center py-16 text-gray-500">
            <AlertCircle className="h-10 w-10 mx-auto text-gray-300 mb-3" />
            <p className="text-sm">Žádné záznamy historie spotřeby.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th
                    onClick={handleSortByDate}
                    className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Datum
                      {sortDescending ? <ChevronDown className="ml-1 h-4 w-4" /> : <ChevronUp className="ml-1 h-4 w-4" />}
                    </div>
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ záznamu</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Materiál</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ spotřeby</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Faktura</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Produkt</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Spotřeba</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Původní mn.</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Nové mn.</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Změna</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ změny</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {items.map((item, index) => (
                  <tr key={`${item.recordType}-${item.packingMaterialId}-${item.createdAt}-${index}`} className="hover:bg-gray-50">
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900">{formatDate(item.date)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.recordTypeText}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm font-medium text-gray-900">{item.materialName}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.consumptionTypeText ?? DASH}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.invoiceId ?? DASH}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.productCode ?? DASH}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-900">{formatNumber(item.amount)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-700">{formatNumber(item.oldQuantity)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-700">{formatNumber(item.newQuantity)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-900">{formatNumber(item.changeAmount)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.logTypeText ?? DASH}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <Pagination
          totalCount={totalCount}
          pageNumber={pageNumber}
          pageSize={pageSize}
          totalPages={totalPages}
          onPageChange={setPageNumber}
          onPageSizeChange={(size) => {
            setPageSize(size);
            setPageNumber(1);
          }}
        />
      </div>
    </div>
  );
};

export default ConsumptionHistoryTab;
