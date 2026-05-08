import React, { useCallback, useEffect, useState } from "react";
import { Search, X, Tag } from "lucide-react";
import type { TagWithCountDto } from "../../../api/hooks/usePhotobank";
import { TagBadge } from "../../ui/TagBadge";

interface TagSidebarProps {
  tags: TagWithCountDto[];
  selectedTagIds: number[];
  search: string;
  withoutTags: boolean;
  useRegex: boolean;
  onTagToggle: (tagId: number) => void;
  onSearchChange: (value: string) => void;
  onWithoutTagsToggle: () => void;
  onClearFilters: () => void;
  onRegexChange: (value: boolean) => void;
  errorMessage?: string | null;
}

const DEBOUNCE_MS = 300;

const TagSidebar: React.FC<TagSidebarProps> = ({
  tags,
  selectedTagIds,
  search,
  withoutTags,
  useRegex,
  onTagToggle,
  onSearchChange,
  onWithoutTagsToggle,
  onClearFilters,
  onRegexChange,
  errorMessage,
}) => {
  const [inputValue, setInputValue] = useState(search);
  const [tagFilter, setTagFilter] = useState("");
  const [regexError, setRegexError] = useState<string | null>(null);

  useEffect(() => {
    setInputValue(search);
  }, [search]);

  useEffect(() => {
    if (!useRegex || !inputValue) {
      setRegexError(null);
      return;
    }
    try {
      new RegExp(inputValue);
      setRegexError(null);
    } catch {
      setRegexError("Neplatný regulární výraz");
    }
  }, [inputValue, useRegex]);

  useEffect(() => {
    const timer = setTimeout(() => {
      if (inputValue !== search && regexError === null) {
        onSearchChange(inputValue);
      }
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [inputValue, search, onSearchChange, regexError]);

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

  const filteredTags = tagFilter.trim()
    ? tags.filter((t) => t.name.toLowerCase().includes(tagFilter.trim().toLowerCase()))
    : tags;

  const hasActiveFilters = search.length > 0 || selectedTagIds.length > 0 || withoutTags || useRegex;

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
            placeholder={useRegex ? "Regex (POSIX, case-insensitive)..." : "Hledat soubory a složky..."}
            className={`w-full pl-8 pr-7 py-1.5 text-sm border rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent ${
              regexError ? "border-red-400" : "border-gray-300"
            }`}
            aria-label="Hledat soubory a složky"
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

        {/* Regex toggle */}
        <label className="flex items-center gap-1.5 mt-2 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={useRegex}
            onChange={(e) => onRegexChange(e.target.checked)}
            className="w-3.5 h-3.5 accent-primary-blue"
          />
          <span className="text-xs text-gray-600">Regex</span>
        </label>
        {regexError && (
          <p className="mt-1 text-xs text-red-600">{regexError}</p>
        )}
        {!regexError && errorMessage && (
          <p className="mt-1 text-xs text-red-600">{errorMessage}</p>
        )}
      </div>

      {/* Tag list */}
      <div className="flex-1 overflow-y-auto p-4">
        <div className="flex items-center gap-1.5 mb-2">
          <Tag className="w-3.5 h-3.5 text-gray-400" />
          <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
            Štítky
          </span>
        </div>

        {/* Tag name filter input */}
        {tags.length > 0 && (
          <div className="relative mb-2">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3 h-3 text-gray-400 pointer-events-none" />
            <input
              type="text"
              value={tagFilter}
              onChange={(e) => setTagFilter(e.target.value)}
              placeholder="Filtrovat štítky..."
              className="w-full pl-7 pr-6 py-1 text-xs border border-gray-200 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-blue focus:border-transparent"
              aria-label="Filtrovat štítky"
            />
            {tagFilter && (
              <button
                type="button"
                onClick={() => setTagFilter("")}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                aria-label="Vymazat filtr štítků"
              >
                <X className="w-3 h-3" />
              </button>
            )}
          </div>
        )}

        {tags.length === 0 ? (
          <p className="text-sm text-gray-400 mt-2">Žádné štítky</p>
        ) : (
          <ul className="space-y-0.5">
            <li>
              <button
                type="button"
                onClick={onWithoutTagsToggle}
                className={[
                  "w-full flex items-center justify-between px-2 py-1.5 rounded-md text-sm transition-colors text-left",
                  withoutTags
                    ? "bg-secondary-blue-pale text-primary-blue font-medium"
                    : "text-gray-700 hover:bg-gray-50",
                ].join(" ")}
                aria-pressed={withoutTags}
              >
                <span className="text-xs italic text-gray-400">Bez štítků</span>
                <span className="ml-2 text-xs tabular-nums flex-shrink-0 text-gray-300">—</span>
              </button>
            </li>

            {filteredTags.map((tag) => {
              const isSelected = selectedTagIds.includes(tag.id);
              return (
                <li key={tag.id}>
                  <button
                    type="button"
                    onClick={() => onTagToggle(tag.id)}
                    className={[
                      "w-full flex items-center justify-between px-2 py-1.5 rounded-md transition-colors text-left",
                      isSelected ? "bg-secondary-blue-pale" : "hover:bg-gray-50",
                    ].join(" ")}
                    aria-pressed={isSelected}
                  >
                    <TagBadge name={tag.name} />
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

            {filteredTags.length === 0 && tagFilter && (
              <li className="px-2 py-1.5 text-xs text-gray-400">Žádné výsledky</li>
            )}
          </ul>
        )}
      </div>
    </aside>
  );
};

export default TagSidebar;
