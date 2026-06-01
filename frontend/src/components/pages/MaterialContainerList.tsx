import React, { useState } from "react";
import { Search, Filter, AlertCircle, Loader2, ChevronLeft, ChevronRight } from "lucide-react";
import {
  useMaterialContainersList,
  MaterialContainersListRequest,
} from "../../api/hooks/useMaterialContainers";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";

const formatDate = (date: Date | string | undefined): string => {
  if (!date) return "-";
  const dateObj = typeof date === "string" ? new Date(date) : date;
  return dateObj.toLocaleString("cs-CZ");
};

const formatAmount = (amount?: number, unit?: string): string => {
  if (amount === undefined || amount === null) return "-";
  return unit ? `${amount} ${unit}` : `${amount}`;
};

const MaterialContainerList: React.FC = () => {
  const [materialInput, setMaterialInput] = useState("");
  const [lotInput, setLotInput] = useState("");
  const [codeInput, setCodeInput] = useState("");

  const [materialFilter, setMaterialFilter] = useState("");
  const [lotFilter, setLotFilter] = useState("");
  const [codeFilter, setCodeFilter] = useState("");

  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  useScreenView("Manufacturing", "MaterialContainers");

  const request: MaterialContainersListRequest = {
    materialCode: materialFilter || undefined,
    lotCode: lotFilter || undefined,
    code: codeFilter || undefined,
    page: pageNumber,
    pageSize,
  };

  const { data, isLoading: loading, error } = useMaterialContainersList(request);

  const containers = data?.containers || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const isFiltered = Boolean(materialFilter || lotFilter || codeFilter);

  const handleApplyFilters = () => {
    setMaterialFilter(materialInput);
    setLotFilter(lotInput);
    setCodeFilter(codeInput);
    setPageNumber(1);
  };

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  const handleClearFilters = () => {
    setMaterialInput("");
    setLotInput("");
    setCodeInput("");
    setMaterialFilter("");
    setLotFilter("");
    setCodeFilter("");
    setPageNumber(1);
  };

  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání šarží...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání šarží: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900">Šarže</h1>
      </div>

      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  value={materialInput}
                  onChange={(e) => setMaterialInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border border-gray-300 rounded-md"
                  placeholder="Materiál"
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <input
                type="text"
                value={lotInput}
                onChange={(e) => setLotInput(e.target.value)}
                onKeyDown={handleKeyDown}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full px-3 py-2 sm:text-sm border border-gray-300 rounded-md"
                placeholder="Šarže"
              />
            </div>

            <div className="flex-1 max-w-xs">
              <input
                type="text"
                value={codeInput}
                onChange={(e) => setCodeInput(e.target.value)}
                onKeyDown={handleKeyDown}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full px-3 py-2 sm:text-sm border border-gray-300 rounded-md"
                placeholder="Kód kontejneru"
              />
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleApplyFilters}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
            >
              Filtrovat
            </button>
            <button
              onClick={handleClearFilters}
              className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
            >
              Vymazat
            </button>
          </div>
        </div>
      </div>

      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Kód kontejneru</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Materiál</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Šarže</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Množství</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Vytvořeno</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Kdo</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {containers.map((container) => (
                <tr key={container.id} className="hover:bg-gray-50 transition-colors duration-150">
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{container.code}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{container.materialCode}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{container.lotCode}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatAmount(container.amount, container.unit)}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(container.createdAt)}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{container.createdBy}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {containers.length === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">Žádné kontejnery nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>

      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
          <div className="flex items-center space-x-3">
            <p className="text-xs text-gray-600">
              {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
              {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
              {isFiltered ? <span className="text-gray-500"> (filtrováno)</span> : ""}
            </p>
            <div className="flex items-center space-x-1">
              <span className="text-xs text-gray-600">Zobrazit:</span>
              <select
                value={pageSize}
                onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
              >
                <option value={20}>20</option>
                <option value={50}>50</option>
                <option value={100}>100</option>
              </select>
            </div>
          </div>
          <nav className="relative z-0 inline-flex rounded shadow-sm -space-x-px" aria-label="Pagination">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronLeft className="h-3 w-3" />
            </button>
            <span className="relative inline-flex items-center px-2 py-1 border border-gray-300 bg-white text-xs font-medium text-gray-700">
              {pageNumber} / {totalPages}
            </span>
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronRight className="h-3 w-3" />
            </button>
          </nav>
        </div>
      )}
    </div>
  );
};

export default MaterialContainerList;
