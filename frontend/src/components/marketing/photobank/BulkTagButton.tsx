import React from "react";
import { Tag } from "lucide-react";

interface BulkTagButtonProps {
  search: string;
  folderPath: string;
  selectedTagNames: string[];
  totalMatching: number;
  onOpenDialog: () => void;
}

const NO_FILTER_TOOLTIP = "Nejprve použijte filtr";

function isFilterActive(search: string, folderPath: string, selectedTagNames: string[]): boolean {
  return search !== "" || folderPath !== "" || selectedTagNames.length > 0;
}

export default function BulkTagButton({
  search,
  folderPath,
  selectedTagNames,
  totalMatching,
  onOpenDialog,
}: BulkTagButtonProps) {
  const filterActive = isFilterActive(search, folderPath, selectedTagNames);
  const isDisabled = !filterActive || totalMatching === 0;
  const tooltip = isDisabled ? NO_FILTER_TOOLTIP : undefined;

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
