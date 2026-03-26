import React, { useState, useEffect } from "react";
import { FileText, Printer, ExternalLink, ChevronLeft, ChevronRight } from "lucide-react";
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../api/hooks/useExpeditionListArchive";
import { useToast } from "../contexts/ToastContext";

const PAGE_SIZE = 20;

const formatFileSize = (bytes: number | null): string => {
  if (bytes === null) return "–";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const formatDateTime = (iso: string | null): string => {
  if (!iso) return "–";
  return new Date(iso).toLocaleString("cs-CZ", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
};

const ExpeditionListArchivePage: React.FC = () => {
  const { showSuccess, showError } = useToast();
  const [page, setPage] = useState(1);
  const [selectedDate, setSelectedDate] = useState<string>("");
  const [reprintConfirm, setReprintConfirm] = useState<ExpeditionListItemDto | null>(null);

  const { data: datesData, isLoading: datesLoading } = useExpeditionDates(page, PAGE_SIZE);
  const { data: itemsData, isLoading: itemsLoading } = useExpeditionListsByDate(selectedDate);
  const reprintMutation = useReprintExpeditionList();

  // Auto-select the first (most recent) date when dates load
  useEffect(() => {
    if (datesData?.dates?.length && !selectedDate) {
      setSelectedDate(datesData.dates[0]);
    }
  }, [datesData, selectedDate]);

  const totalPages = datesData ? Math.ceil(datesData.totalCount / PAGE_SIZE) : 0;

  const handleOpen = (item: ExpeditionListItemDto) => {
    const url = getExpeditionListDownloadUrl(item.blobPath);
    window.open(url, "_blank", "noopener,noreferrer");
  };

  const handleReprintConfirm = async () => {
    if (!reprintConfirm) return;
    try {
      await reprintMutation.mutateAsync({ blobPath: reprintConfirm.blobPath });
      showSuccess("Přetisk odeslán", `${reprintConfirm.fileName} byl odeslán na tiskárnu.`);
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.message
          : typeof err === 'object' && err !== null && 'message' in err
            ? String((err as { message: unknown }).message)
            : 'Nepodařilo se odeslat na tisk.';
      showError("Chyba tisku", msg);
    } finally {
      setReprintConfirm(null);
    }
  };

  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold text-gray-900 mb-6">Archiv expedičních listů</h1>

      <div className="flex gap-6">
        {/* Date list sidebar */}
        <div className="w-56 flex-shrink-0">
          <h2 className="text-sm font-medium text-gray-700 mb-2">Datum</h2>
          <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
            {datesLoading ? (
              <div className="p-4 text-center text-gray-500 text-sm">Načítám...</div>
            ) : !datesData?.dates.length ? (
              <div className="p-4 text-center text-gray-500 text-sm">Žádná data</div>
            ) : (
              <ul>
                {datesData.dates.map((date) => (
                  <li key={date}>
                    <button
                      onClick={() => setSelectedDate(date)}
                      className={`w-full text-left px-4 py-2 text-sm hover:bg-gray-50 transition-colors ${
                        selectedDate === date
                          ? "bg-indigo-50 text-indigo-700 font-medium"
                          : "text-gray-700"
                      }`}
                    >
                      {date}
                    </button>
                  </li>
                ))}
              </ul>
            )}

            {/* Pagination for dates */}
            {totalPages > 1 && (
              <div className="flex items-center justify-between px-4 py-2 border-t border-gray-200">
                <button
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page === 1}
                  className="text-gray-500 disabled:opacity-30 hover:text-gray-700"
                >
                  <ChevronLeft size={16} />
                </button>
                <span className="text-xs text-gray-500">
                  {page}/{totalPages}
                </span>
                <button
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                  className="text-gray-500 disabled:opacity-30 hover:text-gray-700"
                >
                  <ChevronRight size={16} />
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Items table */}
        <div className="flex-1">
          {!selectedDate ? (
            <div className="flex items-center justify-center h-48 text-gray-500">
              Vyberte datum
            </div>
          ) : itemsLoading ? (
            <div className="flex items-center justify-center h-48">
              <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-indigo-600"></div>
            </div>
          ) : !itemsData?.items.length ? (
            <div className="flex flex-col items-center justify-center h-48 text-gray-500">
              <FileText size={40} className="mb-2 text-gray-300" />
              <p>Žádné soubory pro {selectedDate}</p>
            </div>
          ) : (
            <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Soubor</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Nahráno</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Velikost</th>
                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">Akce</th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-100">
                  {itemsData.items.map((item) => (
                    <tr key={item.blobPath} className="hover:bg-gray-50">
                      <td className="px-4 py-3 text-sm text-gray-900 flex items-center gap-2">
                        <FileText size={16} className="text-red-400 flex-shrink-0" />
                        {item.fileName}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500">
                        {formatDateTime(item.createdOn)}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500">
                        {formatFileSize(item.contentLength)}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-2">
                          <button
                            onClick={() => handleOpen(item)}
                            className="inline-flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-indigo-600 bg-indigo-50 rounded hover:bg-indigo-100 transition-colors"
                          >
                            <ExternalLink size={12} />
                            Otevřít
                          </button>
                          <button
                            onClick={() => setReprintConfirm(item)}
                            className="inline-flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-gray-700 bg-gray-100 rounded hover:bg-gray-200 transition-colors"
                          >
                            <Printer size={12} />
                            Přetisk
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {/* Reprint confirmation dialog */}
      {reprintConfirm && (
        <div className="fixed inset-0 bg-black bg-opacity-40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Potvrdit přetisk</h3>
            <p className="text-sm text-gray-600 mb-4">
              Odeslat <span className="font-medium">{reprintConfirm.fileName}</span> znovu na tiskárnu?
            </p>
            <div className="flex gap-3 justify-end">
              <button
                onClick={() => setReprintConfirm(null)}
                disabled={reprintMutation.isPending}
                className="px-4 py-2 text-sm text-gray-700 bg-gray-100 rounded hover:bg-gray-200 disabled:opacity-50"
              >
                Zrušit
              </button>
              <button
                onClick={handleReprintConfirm}
                disabled={reprintMutation.isPending}
                className="px-4 py-2 text-sm text-white bg-indigo-600 rounded hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
              >
                {reprintMutation.isPending ? (
                  <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                ) : (
                  <Printer size={14} />
                )}
                Přetisknout
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ExpeditionListArchivePage;
