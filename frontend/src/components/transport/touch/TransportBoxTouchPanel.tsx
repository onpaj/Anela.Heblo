import React from "react";
import { Table2 } from "lucide-react";
import OpenBoxByCodeField from "./OpenBoxByCodeField";
import OpenBoxCardList from "./OpenBoxCardList";

interface TransportBoxTouchPanelProps {
  // Opens the (touch-friendly) detail modal for the given box.
  onOpenBox: (boxId: number) => void;
  // Reveals the full filterable table for power use on touch devices.
  onShowAll: () => void;
}

// Touch-friendly entry point for the transport-box workflow. Replaces the dense
// table on narrow screens with the two frequent actions: open a box by its code
// (create or resume) and resume one of the currently-open boxes from a card list.
const TransportBoxTouchPanel: React.FC<TransportBoxTouchPanelProps> = ({
  onOpenBox,
  onShowAll,
}) => {
  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      {/* Open-by-code action */}
      <div className="flex-shrink-0 rounded-lg bg-white p-4 shadow dark:bg-graphite-surface dark:shadow-soft-dark">
        <OpenBoxByCodeField onOpenBox={onOpenBox} />
      </div>

      {/* Open boxes to resume */}
      <div className="flex min-h-0 flex-1 flex-col rounded-lg bg-white shadow dark:bg-graphite-surface dark:shadow-soft-dark">
        <h2 className="flex-shrink-0 border-b border-gray-200 p-4 text-sm font-medium text-gray-700 dark:border-graphite-border dark:text-graphite-text">
          Otevřené boxy
        </h2>
        <div className="min-h-0 flex-1 overflow-y-auto p-4">
          <OpenBoxCardList onOpenBox={onOpenBox} />
        </div>
      </div>

      {/* Escape hatch to the full table */}
      <button
        type="button"
        onClick={onShowAll}
        className="flex flex-shrink-0 items-center justify-center gap-2 rounded-lg border border-gray-300 bg-white py-3 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:hover:bg-white/5"
      >
        <Table2 className="h-4 w-4" />
        Zobrazit všechny boxy
      </button>
    </div>
  );
};

export default TransportBoxTouchPanel;
