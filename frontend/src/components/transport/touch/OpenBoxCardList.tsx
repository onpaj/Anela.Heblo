import React from "react";
import { RefreshCw, PackageOpen } from "lucide-react";
import { useTransportBoxesQuery } from "../../../api/hooks/useTransportBoxes";
import { TransportBoxState } from "../../../api/generated/api-client";
import OpenBoxCard from "./OpenBoxCard";

interface OpenBoxCardListProps {
  onOpenBox: (boxId: number) => void;
}

// Fetches and renders the currently-open boxes as large tappable cards — the
// "resume work" half of the touch entry panel. Open boxes are the only ones a
// user can add items to, so this is intentionally a small, focused set.
const OpenBoxCardList: React.FC<OpenBoxCardListProps> = ({ onOpenBox }) => {
  const { data, isLoading, error } = useTransportBoxesQuery({
    skip: 0,
    take: 50,
    state: TransportBoxState.Opened,
    sortBy: "laststatechanged",
    sortDescending: true,
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-10 text-gray-500 dark:text-graphite-muted">
        <RefreshCw className="h-6 w-6 animate-spin" />
        <span className="ml-2 text-sm">Načítání otevřených boxů…</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/40 dark:bg-red-900/30 dark:text-red-300">
        Nepodařilo se načíst otevřené boxy.
      </div>
    );
  }

  const boxes = data?.items ?? [];

  if (boxes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-10 text-center text-gray-500 dark:text-graphite-muted">
        <PackageOpen className="mb-3 h-10 w-10 text-gray-300 dark:text-graphite-faint" />
        <p className="text-sm">
          Žádný otevřený box — naskenujte nebo zadejte kód a začněte.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      {boxes.map((box) => (
        <OpenBoxCard key={box.id} box={box} onOpenBox={onOpenBox} />
      ))}
    </div>
  );
};

export default OpenBoxCardList;
