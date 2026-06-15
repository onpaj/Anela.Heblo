import React from "react";
import { ChevronUp, ChevronDown } from "lucide-react";

interface SortableHeaderProps {
  column: string;
  sortBy: string;
  sortDescending: boolean;
  onSort: (column: string) => void;
  children: React.ReactNode;
  className?: string;
}

// Shared click-to-sort table header used by the access-management grids.
// Mirrors the SortableHeader pattern from CatalogList.
const SortableHeader: React.FC<SortableHeaderProps> = ({
  column,
  sortBy,
  sortDescending,
  onSort,
  children,
  className = "",
}) => {
  const isActive = sortBy === column;
  const isAscending = isActive && !sortDescending;
  const isDescending = isActive && sortDescending;

  return (
    <th
      scope="col"
      className={`px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none ${className}`}
      onClick={() => onSort(column)}
    >
      <div className="flex items-center space-x-1">
        <span>{children}</span>
        <div className="flex flex-col">
          <ChevronUp
            className={`h-3 w-3 ${isAscending ? "text-indigo-600" : "text-gray-300"}`}
          />
          <ChevronDown
            className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600" : "text-gray-300"}`}
          />
        </div>
      </div>
    </th>
  );
};

export default SortableHeader;
