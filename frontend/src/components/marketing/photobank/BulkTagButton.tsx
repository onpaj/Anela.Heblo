import React from "react";
import { Tag } from "lucide-react";

interface BulkTagButtonProps {
  search: string;
  selectedTagNames: string[];
  totalMatching: number;
  onOpenDialog: () => void;
}

const NO_FILTER_TOOLTIP = "Nejprve použijte filtr";
const ZERO_MATCH_TOOLTIP = "Žádné fotky neodpovídají filtru";

function isFilterActive(search: string, selectedTagNames: string[]): boolean {
  return search !== "" || selectedTagNames.length > 0;
}

export default function BulkTagButton({
  search,
  selectedTagNames,
  totalMatching,
  onOpenDialog,
}: BulkTagButtonProps) {
  const filterActive = isFilterActive(search, selectedTagNames);
  const isDisabled = !filterActive || totalMatching === 0;
  const tooltip = !filterActive
    ? NO_FILTER_TOOLTIP
    : totalMatching === 0
      ? ZERO_MATCH_TOOLTIP
      : undefined;

  return (
    <button
      type="button"
      disabled={isDisabled}
      title={tooltip}
      onClick={onOpenDialog}
      className="flex items-center gap-1.5 px-2 py-1 text-sm border border-gray-200 rounded-md hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
    >
      <Tag className="w-4 h-4" />
      Otagovat
    </button>
  );
}
