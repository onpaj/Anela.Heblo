import React, { useCallback, useEffect, useState } from "react";
import { Search, X, Tag } from "lucide-react";
import type { TagWithCountDto } from "../../../api/hooks/usePhotobank";

interface TagSidebarProps {
  tags: TagWithCountDto[];
  selectedTagIds: number[];
  search: string;
  onTagToggle: (tagId: number) => void;
  onSearchChange: (value: string) => void;
  onClearFilters: () => void;
}

const DEBOUNCE_MS = 300;

const TagSidebar: React.FC<TagSidebarProps> = ({
  tags,
  selectedTagIds,
  search,
  onTagToggle,
  onSearchChange,
  onClearFilters,
}) => {
  const [inputValue, setInputValue] = useState(search);

  // Sync external search value to local input
  useEffect(() => {
    setInputValue(search);
  }, [search]);

  // Debounce search input changes
  useEffect(() => {
    const timer = setTimeout(() => {
      if (inputValue !== search) {
        onSearchChange(inputValue);
      }
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [inputValue, search, onSearchChange]);

  const handleInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setInputValue(e.target.value);
    },
    [],
  );

  const handleClearSearch = useCallback(() => {
    setInputValue("");
    onSearchChange("");
  }, [onSearchChange]);

  const hasActiveFilters = search.length > 0 || selectedTagIds.length > 0;

  return (
    <aside className="flex flex-col h-full bg-white border-r border-gray-200 overflow-hidden">
      <div className="p-4 border-b border-gray-100">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wide">
            Filtry
          </h2>
          {hasActiveFilters && (
            <button
              onClick={onClearFilters}
              className="text-xs text-primary-blue hover:underline flex items-center gap-1"
              aria-label="Vymazat filtry"
            >
              <X className="w-3 h-3" />
              Vymazat
            </button>
          )}
        </div>

        {/* Search input */}
        <div className="relative">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 pointer-events-none" />
          <input
            type="text"
            value={inputValue}
            onChange={handleInputChange}
            placeholder="Hledat soubory..."
            className="w-full pl-8 pr-7 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            aria-label="Hledat soubory"
          />
          {inputValue && (
            <button
              onClick={handleClearSearch}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
              aria-label="Vymazat hledání"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </div>

      {/* Tag list */}
      <div className="flex-1 overflow-y-auto p-4">
        <div className="flex items-center gap-1.5 mb-2">
          <Tag className="w-3.5 h-3.5 text-gray-400" />
          <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
            Štítky
          </span>
        </div>

        {tags.length === 0 ? (
          <p className="text-sm text-gray-400 mt-2">Žádné štítky</p>
        ) : (
          <ul className="space-y-0.5">
            {tags.map((tag) => {
              const isSelected = selectedTagIds.includes(tag.id);
              return (
                <li key={tag.id}>
                  <button
                    onClick={() => onTagToggle(tag.id)}
                    className={[
                      "w-full flex items-center justify-between px-2 py-1.5 rounded-md text-sm transition-colors text-left",
                      isSelected
                        ? "bg-secondary-blue-pale text-primary-blue font-medium"
                        : "text-gray-700 hover:bg-gray-50",
                    ].join(" ")}
                    aria-pressed={isSelected}
                  >
                    <span className="truncate">{tag.name}</span>
                    <span
                      className={[
                        "ml-2 text-xs tabular-nums flex-shrink-0",
                        isSelected ? "text-primary-blue" : "text-gray-400",
                      ].join(" ")}
                    >
                      {tag.count}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </aside>
  );
};

export default TagSidebar;
