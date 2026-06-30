import React from "react";
import { Package, Calendar, ChevronRight, MapPin } from "lucide-react";
import { GetTransportBoxesResponse } from "../../../api/generated/api-client";

// The shape of a single box as returned by the transport-box list query.
export type TransportBoxListItem = NonNullable<
  GetTransportBoxesResponse["items"]
>[number];

interface OpenBoxCardProps {
  box: TransportBoxListItem;
  onOpenBox: (boxId: number) => void;
}

const formatDate = (dateString: string | undefined): string => {
  if (!dateString) return "-";
  return new Date(dateString).toLocaleDateString("cs-CZ", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
};

// A large, tappable card representing one open transport box. Touch-friendly
// alternative to a dense table row — used in the box-list touch entry panel.
const OpenBoxCard: React.FC<OpenBoxCardProps> = ({ box, onOpenBox }) => {
  if (!box.id) return null;

  const boxId = box.id;

  return (
    <button
      type="button"
      onClick={() => onOpenBox(boxId)}
      className="w-full flex items-center gap-3 rounded-xl border border-gray-200 bg-white p-4 text-left shadow-sm transition-colors hover:bg-indigo-50 active:bg-indigo-100 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2 dark:hover:bg-graphite-accent/10 dark:active:bg-graphite-accent/20"
    >
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="font-mono text-lg font-semibold text-gray-900 dark:text-graphite-text">
            {box.code || "-"}
          </span>
          <span className="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-800 dark:bg-blue-900/30 dark:text-blue-300">
            Otevřený
          </span>
        </div>

        {box.description && (
          <p className="mt-1 truncate text-sm text-gray-600 dark:text-graphite-muted">
            {box.description}
          </p>
        )}

        <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-gray-500 dark:text-graphite-muted">
          <span className="flex items-center">
            <Package className="mr-1 h-3.5 w-3.5 text-gray-400 dark:text-graphite-faint" />
            {box.itemCount} položek
          </span>
          {box.location && (
            <span className="flex items-center">
              <MapPin className="mr-1 h-3.5 w-3.5 text-gray-400 dark:text-graphite-faint" />
              {box.location}
            </span>
          )}
          <span className="flex items-center">
            <Calendar className="mr-1 h-3.5 w-3.5 text-gray-400 dark:text-graphite-faint" />
            {formatDate(box.lastStateChanged?.toString())}
          </span>
        </div>
      </div>

      <ChevronRight className="h-5 w-5 flex-shrink-0 text-gray-400 dark:text-graphite-faint" />
    </button>
  );
};

export default OpenBoxCard;
