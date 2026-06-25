import React, { useState } from "react";
import {
  useJournalEntries,
  useSearchJournalEntries,
  useJournalEntry,
} from "../../../api/hooks/useJournal";
import {
  Plus,
  Search,
  Filter,
  Loader2,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  ChevronDown,
} from "lucide-react";
import { format } from "date-fns";
// Import removed - using default date format for now to fix Jest test issues
import type {
  JournalEntryDto,
  SearchJournalEntryDto,
} from "../../../api/generated/api-client";
import JournalEntryModal from "../../JournalEntryModal";
import { useScreenView } from '../../../telemetry/useScreenView';
import { truncateContent } from "./journalPreview";

interface JournalRowProps {
  id: number;
  title?: string;
  entryDate: Date | string;
  authorLabel: string;
  contentText: string;
  tags?: { id?: number; name?: string; color?: string }[];
  associatedProducts?: string[];
  onClick: () => void;
}

const JournalRow: React.FC<JournalRowProps> = ({
  id,
  title,
  entryDate,
  authorLabel,
  contentText,
  tags,
  associatedProducts,
  onClick,
}) => (
  <tr
    className="hover:bg-gray-50 dark:hover:bg-white/5 cursor-pointer transition-colors duration-150"
    data-testid="journal-entry"
    data-entry-id={id}
    onClick={onClick}
    title="Klikněte pro editaci záznamu"
  >
    <td className="px-4 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
      <div className="max-w-48 truncate">
        {title || "Bez názvu"}
      </div>
    </td>
    <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
      {format(new Date(entryDate), "dd.MM.yyyy")}
    </td>
    <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">
      <div className="max-w-32 truncate">
        {authorLabel}
      </div>
    </td>
    <td className="px-4 py-4 text-sm text-gray-700 dark:text-graphite-muted">
      <div className="max-w-96 line-clamp-2">
        {contentText}
      </div>
    </td>
    <td className="px-4 py-4 text-sm">
      <div className="flex flex-wrap gap-1 max-w-48">
        {tags &&
          tags.slice(0, 2).map((tag, index) => (
            <span
              key={tag.id ?? index}
              className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium border"
              style={{
                borderColor: tag.color,
                color: tag.color,
              }}
            >
              {tag.name}
            </span>
          ))}
        {tags && tags.length > 2 && (
          <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-600 dark:text-graphite-muted">
            +{tags.length - 2}
          </span>
        )}
      </div>
    </td>
    <td className="px-4 py-4 text-sm">
      <div className="flex flex-wrap gap-1 max-w-32">
        {associatedProducts
          ?.slice(0, 2)
          .map((product) => (
            <span
              key={product}
              className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-indigo-100 dark:bg-graphite-accent/20 text-indigo-800 dark:text-graphite-accent"
            >
              {product}
            </span>
          ))}
        {associatedProducts &&
          associatedProducts.length > 2 && (
            <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-600 dark:text-graphite-muted">
              +{associatedProducts.length - 2}
            </span>
          )}
      </div>
    </td>
  </tr>
);

interface SortableHeaderProps {
  column: string;
  sortBy: string;
  sortDescending: boolean;
  onSort: (column: string) => void;
  children: React.ReactNode;
}

const SortableHeader: React.FC<SortableHeaderProps> = ({
  column,
  sortBy,
  sortDescending,
  onSort,
  children,
}) => {
  const isActive = sortBy === column;
  const isAscending = isActive && !sortDescending;
  const isDescending = isActive && sortDescending;

  return (
    <th
      scope="col"
      className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5 select-none"
      onClick={() => onSort(column)}
    >
      <div className="flex items-center space-x-1">
        <span>{children}</span>
        <div className="flex flex-col">
          <ChevronUp
            className={`h-3 w-3 ${isAscending ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-300 dark:text-graphite-faint"}`}
          />
          <ChevronDown
            className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-300 dark:text-graphite-faint"}`}
          />
        </div>
      </div>
    </th>
  );
};

const JournalList: React.FC = () => {
  useScreenView('Journal', 'JournalList');

  // Filter states - separate input values from applied filters
  const [searchTextInput, setSearchTextInput] = useState("");
  const [searchTextFilter, setSearchTextFilter] = useState("");
  const [isSearchMode, setIsSearchMode] = useState(false);

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states
  const [sortBy, setSortBy] = useState<string>("EntryDate");
  const [sortDescending, setSortDescending] = useState(true);

  // Modal states for edit/create
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingEntryId, setEditingEntryId] = useState<number | null>(null);

  // Fetch entry data when editing
  const { data: editingEntry } = useJournalEntry(editingEntryId || 0);

  // Use regular entries by default, search entries when searching
  const entriesQuery = useJournalEntries({
    pageNumber: pageNumber,
    pageSize: pageSize,
    sortBy: sortBy,
    sortDirection: sortDescending ? "DESC" : "ASC",
  });

  const searchQuery = useSearchJournalEntries(
    {
      searchText: searchTextFilter,
      pageNumber: pageNumber,
      pageSize: pageSize,
      sortBy: sortBy,
      sortDirection: sortDescending ? "DESC" : "ASC",
    },
    isSearchMode,
  );

  const currentQuery = isSearchMode ? searchQuery : entriesQuery;
  const entries = currentQuery.data?.entries || [];
  const totalCount = currentQuery.data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);
  const loading = currentQuery.isLoading;
  const error = currentQuery.error;

  // Handler for applying filters
  const handleApplyFilters = () => {
    setSearchTextFilter(searchTextInput);
    setIsSearchMode(searchTextInput.trim() !== "");
    setPageNumber(1); // Reset to first page when applying filters
  };

  // Handler for Enter key press
  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handler for clearing all filters
  const handleClearFilters = () => {
    setSearchTextInput("");
    setSearchTextFilter("");
    setIsSearchMode(false);
    setPageNumber(1); // Reset to first page when clearing filters
  };

  // Sorting handler
  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
  };

  // Pagination handlers
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1); // Reset to first page when changing page size
  };

  // Modal handlers
  const handleOpenNewModal = () => {
    setEditingEntryId(null);
    setIsModalOpen(true);
  };

  const handleOpenEditModal = (entryId: number) => {
    setEditingEntryId(entryId);
    setIsModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setEditingEntryId(null);
  };

  // Loading state
  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání deníku...</div>
        </div>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání deníku: {error.message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full w-full">
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <div className="flex justify-between items-center">
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Deník</h1>
          <button
            onClick={handleOpenNewModal}
            className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
            data-testid="add-journal-entry"
          >
            <Plus className="h-4 w-4 mr-2" />
            Nový záznam
          </button>
        </div>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">Filtry:</span>
            </div>

            <div className="flex-1 max-w-md">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  placeholder="Hledat v záznamech..."
                  value={searchTextInput}
                  onChange={(e) => setSearchTextInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  data-testid="journal-search"
                />
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleApplyFilters}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
            >
              Filtrovat
            </button>
            <button
              onClick={handleClearFilters}
              className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
            >
              Vymazat
            </button>
          </div>
        </div>
      </div>

      {/* Data Grid - Scrollable */}
      <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          {entries.length === 0 ? (
            <div className="p-8 text-center">
              <div className="text-gray-500 dark:text-graphite-muted mb-4">
                {isSearchMode
                  ? "Nenalezeny žádné záznamy odpovídající vašemu hledání."
                  : "Zatím nemáte žádné záznamy v deníku."}
              </div>
              {!isSearchMode && (
                <button
                  onClick={handleOpenNewModal}
                  className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700"
                >
                  Vytvořit první záznam
                </button>
              )}
            </div>
          ) : (
            <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
              <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
                <tr>
                  <SortableHeader
                    column="title"
                    sortBy={sortBy}
                    sortDescending={sortDescending}
                    onSort={handleSort}
                  >
                    Název
                  </SortableHeader>
                  <SortableHeader
                    column="entryDate"
                    sortBy={sortBy}
                    sortDescending={sortDescending}
                    onSort={handleSort}
                  >
                    Datum
                  </SortableHeader>
                  <SortableHeader
                    column="createdByUsername"
                    sortBy={sortBy}
                    sortDescending={sortDescending}
                    onSort={handleSort}
                  >
                    Autor
                  </SortableHeader>
                  <th
                    scope="col"
                    className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
                  >
                    Obsah
                  </th>
                  <th
                    scope="col"
                    className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
                  >
                    Tagy
                  </th>
                  <th
                    scope="col"
                    className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
                  >
                    Produkty
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
                {isSearchMode
                  ? (entries as SearchJournalEntryDto[]).map((entry) => (
                      <JournalRow
                        key={entry.id!}
                        id={entry.id!}
                        title={entry.title ?? undefined}
                        entryDate={entry.entryDate!}
                        authorLabel={entry.createdByUsername || entry.createdByUserId!}
                        contentText={truncateContent(entry.content!, { searchQuery: searchTextFilter })}
                        tags={entry.tags}
                        associatedProducts={entry.associatedProducts}
                        onClick={() => handleOpenEditModal(entry.id!)}
                      />
                    ))
                  : (entries as JournalEntryDto[]).map((entry) => (
                      <JournalRow
                        key={entry.id!}
                        id={entry.id!}
                        title={entry.title ?? undefined}
                        entryDate={entry.entryDate!}
                        authorLabel={entry.createdByUsername || entry.createdByUserId!}
                        contentText={truncateContent(entry.content!)}
                        tags={entry.tags}
                        associatedProducts={entry.associatedProducts}
                        onClick={() => handleOpenEditModal(entry.id!)}
                      />
                    ))}
              </tbody>
            </table>
          )}
        </div>

        {/* Pagination Footer */}
        {totalCount > 0 && (
          <div className="bg-white dark:bg-graphite-surface px-4 py-3 flex items-center justify-between border-t border-gray-200 dark:border-graphite-border sm:px-6">
            <div className="flex items-center justify-between w-full">
              <p className="text-xs text-gray-700 dark:text-graphite-muted">
                Celkem <span className="font-semibold">{totalCount}</span>{" "}
                záznamů
                {isSearchMode ? (
                  <span className="text-gray-500 dark:text-graphite-muted"> (filtrováno)</span>
                ) : (
                  ""
                )}
              </p>
              <div className="flex items-center space-x-1">
                <span className="text-xs text-gray-600 dark:text-graphite-muted">Zobrazit:</span>
                <select
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                  className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                >
                  <option value={10}>10</option>
                  <option value={20}>20</option>
                  <option value={50}>50</option>
                  <option value={100}>100</option>
                </select>
              </div>
            </div>
            <div>
              <nav
                className="relative z-0 inline-flex rounded shadow-sm dark:shadow-soft-dark -space-x-px"
                aria-label="Pagination"
              >
                <button
                  onClick={() => handlePageChange(pageNumber - 1)}
                  disabled={pageNumber <= 1}
                  className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ChevronLeft className="h-3 w-3" />
                </button>

                {/* Page numbers */}
                {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
                  let pageNum: number;
                  if (totalPages <= 5) {
                    pageNum = i + 1;
                  } else if (pageNumber <= 3) {
                    pageNum = i + 1;
                  } else if (pageNumber >= totalPages - 2) {
                    pageNum = totalPages - 4 + i;
                  } else {
                    pageNum = pageNumber - 2 + i;
                  }

                  return (
                    <button
                      key={pageNum}
                      onClick={() => handlePageChange(pageNum)}
                      className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                        pageNum === pageNumber
                          ? "z-10 bg-indigo-50 dark:bg-graphite-accent/10 border-indigo-500 dark:border-graphite-accent text-indigo-600 dark:text-graphite-accent"
                          : "bg-white dark:bg-graphite-surface border-gray-300 dark:border-graphite-border text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
                      }`}
                    >
                      {pageNum}
                    </button>
                  );
                })}

                <button
                  onClick={() => handlePageChange(pageNumber + 1)}
                  disabled={pageNumber >= totalPages}
                  className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ChevronRight className="h-3 w-3" />
                </button>
              </nav>
            </div>
          </div>
        )}
      </div>

      {/* Journal Entry Modal */}
      <JournalEntryModal
        isOpen={isModalOpen}
        onClose={handleCloseModal}
        entry={editingEntryId ? editingEntry : undefined}
        isEdit={!!editingEntryId}
      />
    </div>
  );
};

export default JournalList;
