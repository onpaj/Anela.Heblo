import React, { useEffect } from "react";
import { X, Loader2 } from "lucide-react";
import {
  MarketingCostListItemDto,
  useMarketingCostDetailQuery,
} from "../../api/hooks/useMarketingCosts";

interface MarketingCostDetailProps {
  item: MarketingCostListItemDto | null;
  isOpen: boolean;
  onClose: () => void;
}

const platformBadge: Record<string, string> = {
  GoogleAds: "bg-blue-100 text-blue-800",
  MetaAds: "bg-purple-100 text-purple-800",
};

const MarketingCostDetail: React.FC<MarketingCostDetailProps> = ({ item, isOpen, onClose }) => {
  const { data, isLoading } = useMarketingCostDetailQuery(isOpen && item ? item.id : null);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    if (isOpen) {
      document.addEventListener("keydown", handleEscape);
    }
    return () => document.removeEventListener("keydown", handleEscape);
  }, [isOpen, onClose]);

  if (!isOpen || !item) return null;

  const detail = data?.item;

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) onClose();
  };

  const formatJson = (raw: string | null | undefined): string => {
    if (!raw) return "";
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-[700px] w-full max-h-[95vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-200">
          <div className="flex items-center gap-3">
            <h2 className="text-lg font-semibold">Detail transakce</h2>
            <span
              className={`px-2 py-0.5 rounded text-xs font-medium ${platformBadge[item.platform] || "bg-gray-100 text-gray-800"}`}
            >
              {item.platform}
            </span>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="overflow-y-auto p-5">
          {isLoading ? (
            <div className="flex items-center justify-center h-32">
              <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
            </div>
          ) : (
            <>
              {/* Basic info grid */}
              <div className="grid grid-cols-2 gap-4 mb-6">
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Transaction ID</div>
                  <div className="font-mono text-sm">{item.transactionId}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Platforma</div>
                  <div>{item.platform}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Částka</div>
                  <div className="text-xl font-semibold">
                    {item.amount.toLocaleString("cs-CZ", { minimumFractionDigits: 2 })}{" "}
                    {item.currency || ""}
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Datum transakce</div>
                  <div>{new Date(item.transactionDate).toLocaleDateString("cs-CZ")}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Importováno</div>
                  <div>{new Date(item.importedAt).toLocaleString("cs-CZ")}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Sync status</div>
                  <div>
                    {item.isSynced ? (
                      <span className="bg-green-100 text-green-800 px-2 py-0.5 rounded text-xs font-medium">
                        Synced
                      </span>
                    ) : (
                      <span className="bg-red-100 text-red-800 px-2 py-0.5 rounded text-xs font-medium">
                        Not synced
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {/* Description */}
              {detail?.description && (
                <div className="mb-6">
                  <div className="text-xs uppercase text-gray-500 mb-1">Popis</div>
                  <div className="p-3 bg-gray-50 rounded text-sm">{detail.description}</div>
                </div>
              )}

              {/* Error message */}
              {detail?.errorMessage && (
                <div className="mb-6">
                  <div className="text-xs uppercase text-red-600 mb-1">Chyba</div>
                  <div className="p-3 bg-red-50 border border-red-200 rounded text-sm text-red-800">
                    {detail.errorMessage}
                  </div>
                </div>
              )}

              {/* Raw data */}
              {detail?.rawData && (
                <details className="border border-gray-200 rounded">
                  <summary className="px-4 py-2 cursor-pointer text-sm text-gray-500 select-none">
                    Surová data z API (JSON)
                  </summary>
                  <div className="p-4 bg-gray-900 rounded-b overflow-x-auto">
                    <pre className="text-gray-200 text-xs leading-relaxed whitespace-pre-wrap">
                      {formatJson(detail.rawData)}
                    </pre>
                  </div>
                </details>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
};

export default MarketingCostDetail;
