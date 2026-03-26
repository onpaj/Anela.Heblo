import React, { useState, useEffect } from "react";
import {
  RefreshCw,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  FileText,
  ExternalLink,
  Printer,
} from "lucide-react";
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../../api/hooks/useExpeditionListArchive";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const PAGE_SIZE = 30;

const formatDate = (isoDate: string): string => {
  // isoDate is expected to be "YYYY-MM-DD"
  const [year, month, day] = isoDate.split("-");
  return `${day}.${month}.${year}`;
};

const formatDateTime = (value: string | null): string => {
  if (!value) return "N/A";
  return new Date(value).toLocaleString("cs-CZ");
};

const formatFileSize = (bytes: number | null): string => {
  if (bytes === null) return "N/A";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const getTodayString = (): string => {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
};

const ExpeditionListArchivePage: React.FC = () => {
  const [datesPage, setDatesPage] = useState(1);
  const [selectedDate, setSelectedDate] = useState<string | null>(
    getTodayString(),
  );
  const [hasAutoSelectedDate, setHasAutoSelectedDate] = useState(false);
  const [downloadingBlobPath, setDownloadingBlobPath] = useState<string | null>(null);

  const {
    data: datesData,
    isLoading: datesLoading,
    error: datesError,
    refetch: refetchDates,
  } = useExpeditionDates(datesPage, PAGE_SIZE);

  const {
    data: listsData,
    isLoading: listsLoading,
    error: listsError,
    refetch: refetchLists,
  } = useExpeditionListsByDate(selectedDate);

  const reprintMutation = useReprintExpeditionList();

  // When dates load, if today has no data select the most recent date instead
  useEffect(() => {
    if (!datesData || hasAutoSelectedDate) return;

    const today = getTodayString();
    const dates = datesData.dates;

    if (dates.length === 0) return;

    if (!dates.includes(today)) {
      // Today is not in the list - pick the most recent available date
      // Dates are assumed to come sorted descending; take first
      setSelectedDate(dates[0]);
    }

    setHasAutoSelectedDate(true);
  }, [datesData, hasAutoSelectedDate]);

  const handleDownload = (item: ExpeditionListItemDto) => {
    setDownloadingBlobPath(item.blobPath);
    window.open(getExpeditionListDownloadUrl(item.blobPath), "_blank");
    setTimeout(() => setDownloadingBlobPath(null), 1500);
  };

  const handleReprint = async (item: ExpeditionListItemDto) => {
    const confirmed = window.confirm(
      `Opravdu chcete vytisknout soubor "${item.fileName}"?`,
    );
    if (!confirmed) return;

    try {
      await reprintMutation.mutateAsync({ blobPath: item.blobPath });
    } catch {
      // Error is handled by global toast handler in the API client
    }
  };

  const totalDatesPages = datesData
    ? Math.ceil(datesData.totalCount / PAGE_SIZE)
    : 0;

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Archiv expedic</h1>
      </div>

      <div className="flex gap-4 flex-1 min-h-0">
        {/* Left panel: Date list */}
        <div className="flex-shrink-0 w-52 bg-white rounded-lg shadow flex flex-col">
          <div className="flex items-center justify-between px-3 py-2 border-b border-gray-200">
            <span className="text-xs font-medium text-gray-700">Datum</span>
            <button
              onClick={() => {
                setHasAutoSelectedDate(false);
                refetchDates();
              }}
              disabled={datesLoading}
              title="Obnovit"
              className="p-1 text-gray-400 hover:text-gray-600 rounded"
            >
              <RefreshCw
                className={`h-3 w-3 ${datesLoading ? "animate-spin" : ""}`}
              />
            </button>
          </div>

          <div className="flex-1 overflow-y-auto">
            {datesError ? (
              <div className="p-3 text-xs text-red-600 flex items-start gap-1">
                <AlertCircle className="h-3 w-3 mt-0.5 flex-shrink-0" />
                <span>Chyba při načítání dat</span>
              </div>
            ) : datesLoading ? (
              <div className="flex items-center justify-center p-6">
                <RefreshCw className="h-5 w-5 animate-spin text-gray-400" />
              </div>
            ) : !datesData || datesData.dates.length === 0 ? (
              <div className="p-3 text-xs text-gray-500 text-center">
                Žádná data
              </div>
            ) : (
              <ul>
                {datesData.dates.map((date) => (
                  <li key={date}>
                    <button
                      onClick={() => setSelectedDate(date)}
                      className={`w-full text-left px-3 py-2 text-sm transition-colors duration-150 ${
                        selectedDate === date
                          ? "bg-indigo-50 text-indigo-700 font-medium"
                          : "text-gray-700 hover:bg-gray-50"
                      }`}
                    >
                      {formatDate(date)}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {/* Dates pagination */}
          {totalDatesPages > 1 && (
            <div className="flex-shrink-0 border-t border-gray-200 px-2 py-1.5 flex items-center justify-between">
              <button
                onClick={() => setDatesPage((p) => Math.max(1, p - 1))}
                disabled={datesPage === 1}
                className="p-1 rounded text-gray-500 hover:text-gray-700 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <ChevronLeft className="h-3 w-3" />
              </button>
              <span className="text-xs text-gray-500">
                {datesPage} / {totalDatesPages}
              </span>
              <button
                onClick={() =>
                  setDatesPage((p) => Math.min(totalDatesPages, p + 1))
                }
                disabled={datesPage === totalDatesPages}
                className="p-1 rounded text-gray-500 hover:text-gray-700 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <ChevronRight className="h-3 w-3" />
              </button>
            </div>
          )}
        </div>

        {/* Right panel: Files for selected date */}
        <div className="flex-1 bg-white rounded-lg shadow flex flex-col min-w-0">
          <div className="flex items-center justify-between px-4 py-2 border-b border-gray-200 flex-shrink-0">
            <span className="text-sm font-medium text-gray-700">
              {selectedDate
                ? `Soubory pro ${formatDate(selectedDate)}`
                : "Vyberte datum"}
            </span>
            {selectedDate && (
              <button
                onClick={() => refetchLists()}
                disabled={listsLoading}
                title="Obnovit"
                className="p-1 text-gray-400 hover:text-gray-600 rounded"
              >
                <RefreshCw
                  className={`h-3 w-3 ${listsLoading ? "animate-spin" : ""}`}
                />
              </button>
            )}
          </div>

          <div className="flex-1 overflow-auto min-h-0">
            {!selectedDate ? (
              <div className="flex flex-col items-center justify-center h-full text-center p-6">
                <FileText className="h-10 w-10 text-gray-300 mb-3" />
                <p className="text-sm text-gray-500">
                  Vyberte datum v levém panelu
                </p>
              </div>
            ) : listsError ? (
              <div className="flex flex-col items-center justify-center h-full p-6">
                <AlertCircle className="h-8 w-8 text-red-400 mb-2" />
                <p className="text-sm text-red-600 font-medium">
                  Chyba při načítání souborů
                </p>
                <p className="text-xs text-red-500 mt-1">
                  {listsError instanceof Error
                    ? listsError.message
                    : "Neznámá chyba"}
                </p>
                <button
                  onClick={() => refetchLists()}
                  className="mt-3 px-3 py-1 bg-red-600 text-white text-xs rounded hover:bg-red-700 transition-colors"
                >
                  Zkusit znovu
                </button>
              </div>
            ) : listsLoading ? (
              <div className="flex items-center justify-center h-full">
                <RefreshCw className="h-6 w-6 animate-spin text-gray-400" />
                <span className="ml-2 text-sm text-gray-600">
                  Načítání souborů...
                </span>
              </div>
            ) : !listsData || listsData.items.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-center p-6">
                <FileText className="h-10 w-10 text-gray-300 mb-3" />
                <p className="text-sm text-gray-500">
                  Pro toto datum nejsou žádné soubory
                </p>
              </div>
            ) : (
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50 sticky top-0 z-10">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Soubor
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Nahráno
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Velikost
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Akce
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {listsData.items.map((item) => (
                    <tr key={item.blobPath} className="hover:bg-gray-50">
                      <td className="px-4 py-3 text-sm text-gray-900">
                        <span
                          className="truncate max-w-xs block"
                          title={item.fileName}
                        >
                          {item.fileName}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-600 whitespace-nowrap">
                        {formatDateTime(item.uploadedAt)}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-600 whitespace-nowrap">
                        {formatFileSize(item.sizeBytes)}
                      </td>
                      <td className="px-4 py-3 whitespace-nowrap">
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => handleDownload(item)}
                            disabled={downloadingBlobPath === item.blobPath}
                            className="inline-flex items-center px-2.5 py-1 text-xs font-medium text-indigo-700 bg-indigo-50 border border-indigo-200 rounded hover:bg-indigo-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                          >
                            {downloadingBlobPath === item.blobPath ? (
                              <RefreshCw className="h-3 w-3 mr-1 animate-spin" />
                            ) : (
                              <ExternalLink className="h-3 w-3 mr-1" />
                            )}
                            Otevrit
                          </button>
                          <button
                            onClick={() => handleReprint(item)}
                            disabled={reprintMutation.isPending}
                            className="inline-flex items-center px-2.5 py-1 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                          >
                            <Printer className="h-3 w-3 mr-1" />
                            Vytisknout
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default ExpeditionListArchivePage;
