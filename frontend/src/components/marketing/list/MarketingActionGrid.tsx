import React from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";

export interface MarketingActionDto {
  id?: number;
  title?: string;
  detail?: string;
  actionType?: string;
  dateFrom?: string | Date;
  dateTo?: string | Date;
  associatedProducts?: string[];
  folderLinks?: Array<{
    path?: string;
    label?: string;
    folderType?: string;
  }>;
}

const ACTION_TYPE_BADGE: Record<string, string> = {
  SocialMedia: "bg-blue-100 text-blue-800",
  Event: "bg-purple-100 text-purple-800",
  Email: "bg-green-100 text-green-800",
  PR: "bg-yellow-100 text-yellow-800",
  Photoshoot: "bg-pink-100 text-pink-800",
  Other: "bg-gray-100 text-gray-800",
};

const ACTION_TYPE_LABELS: Record<string, string> = {
  SocialMedia: "Sociální sítě",
  Event: "Událost",
  Email: "Email",
  PR: "PR",
  Photoshoot: "Fotografie",
  Other: "Ostatní",
};

const formatDate = (d: string | Date | null | undefined) => {
  if (!d) return "—";
  const date = typeof d === "string" ? new Date(d) : d;
  return date.toLocaleDateString("cs-CZ");
};

interface MarketingActionGridProps {
  actions: MarketingActionDto[];
  totalPages: number;
  pageNumber: number;
  onPageChange: (page: number) => void;
  onActionClick: (id: number) => void;
  isLoading?: boolean;
}

const MarketingActionGrid: React.FC<MarketingActionGridProps> = ({
  actions,
  totalPages,
  pageNumber,
  onPageChange,
  onActionClick,
  isLoading,
}) => {
  if (isLoading) {
    return (
      <div className="p-8 text-center text-gray-500 text-sm">Načítání...</div>
    );
  }

  if (actions.length === 0) {
    return (
      <div className="p-8 text-center text-gray-500 text-sm">
        Žádné marketingové akce nebyly nalezeny.
      </div>
    );
  }

  return (
    <div>
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              {["Název", "Typ", "Od", "Do", "Produkty"].map((h) => (
                <th
                  key={h}
                  className="px-4 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wider"
                >
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-100">
            {actions.map((action) => (
              <tr
                key={action.id}
                onClick={() => onActionClick(action.id!)}
                className="hover:bg-gray-50 cursor-pointer transition-colors"
              >
                <td className="px-4 py-3 text-sm font-medium text-gray-900">
                  {action.title}
                </td>
                <td className="px-4 py-3">
                  <span
                    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                      ACTION_TYPE_BADGE[action.actionType ?? ""] ??
                      ACTION_TYPE_BADGE.Other
                    }`}
                  >
                    {ACTION_TYPE_LABELS[action.actionType ?? ""] ??
                      action.actionType}
                  </span>
                </td>
                <td className="px-4 py-3 text-sm text-gray-600">
                  {formatDate(action.dateFrom as string)}
                </td>
                <td className="px-4 py-3 text-sm text-gray-600">
                  {formatDate(action.dateTo as string)}
                </td>
                <td className="px-4 py-3 text-sm text-gray-600">
                  {action.associatedProducts?.join(", ") || "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-4 px-4 py-3 border-t border-gray-200">
          <button
            onClick={() => onPageChange(pageNumber - 1)}
            disabled={pageNumber <= 1}
            className="p-1 rounded hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            <ChevronLeft className="h-4 w-4 text-gray-600" />
          </button>
          <span className="text-sm text-gray-600">
            {pageNumber} / {totalPages}
          </span>
          <button
            onClick={() => onPageChange(pageNumber + 1)}
            disabled={pageNumber >= totalPages}
            className="p-1 rounded hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            <ChevronRight className="h-4 w-4 text-gray-600" />
          </button>
        </div>
      )}
    </div>
  );
};

export default MarketingActionGrid;
