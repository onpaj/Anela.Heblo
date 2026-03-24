import React, { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Trash2, ChevronUp, ChevronDown, ChevronLeft, ChevronRight, Filter, Search } from 'lucide-react';
import {
  useKnowledgeBaseDocumentsQuery,
  useKnowledgeBaseContentTypesQuery,
  useDeleteKnowledgeBaseDocumentMutation,
  DocumentSummary,
} from '../../api/hooks/useKnowledgeBase';

const StatusBadge: React.FC<{ status: string }> = ({ status }) => {
  const colorMap: Record<string, string> = {
    indexed: 'bg-green-100 text-green-800',
    processing: 'bg-yellow-100 text-yellow-800',
    failed: 'bg-red-100 text-red-800',
  };
  const classes = colorMap[status.toLowerCase()] ?? 'bg-gray-100 text-gray-800';
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${classes}`}>
      {status}
    </span>
  );
};

const ConfirmDeleteDialog: React.FC<{
  document: DocumentSummary;
  onConfirm: () => void;
  onCancel: () => void;
}> = ({ document, onConfirm, onCancel }) => (
  <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
    <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full">
      <h2 className="text-lg font-semibold mb-2">Smazat dokument?</h2>
      <p className="text-sm text-gray-600 mb-4">
        Opravdu chcete smazat <strong>{document.filename}</strong>? Tato akce je nevratná.
      </p>
      <div className="flex justify-end gap-2">
        <button
          onClick={onCancel}
          className="px-4 py-2 text-sm rounded border border-gray-300 hover:bg-gray-50"
        >
          Zrušit
        </button>
        <button
          onClick={onConfirm}
          className="px-4 py-2 text-sm rounded bg-red-600 text-white hover:bg-red-700"
        >
          Smazat
        </button>
      </div>
    </div>
  </div>
);

interface Props {
  canDelete: boolean;
}

const DEFAULT_SORT_BY = 'CreatedAt';
const DEFAULT_SORT_DESC = true;
const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 20;

const KnowledgeBaseDocumentsTab: React.FC<Props> = ({ canDelete }) => {
  const [searchParams, setSearchParams] = useSearchParams();

  const getInitialPage = () => {
    const p = searchParams.get('kbPage');
    return p ? Math.max(1, parseInt(p, 10)) : DEFAULT_PAGE;
  };
  const getInitialPageSize = () => {
    const ps = searchParams.get('kbPageSize');
    const val = ps ? parseInt(ps, 10) : DEFAULT_PAGE_SIZE;
    return [10, 20, 50].includes(val) ? val : DEFAULT_PAGE_SIZE;
  };
  const getInitialSortBy = () => searchParams.get('kbSortBy') || DEFAULT_SORT_BY;
  const getInitialSortDesc = () => searchParams.get('kbSortDesc') !== 'false';
  const getInitialFilenameFilter = () => searchParams.get('kbFilename') || '';
  const getInitialStatusFilter = () => searchParams.get('kbStatus') || '';
  const getInitialContentTypeFilter = () => searchParams.get('kbContentType') || '';

  const [pageNumber, setPageNumber] = useState(getInitialPage);
  const [pageSize, setPageSize] = useState(getInitialPageSize);
  const [sortBy, setSortBy] = useState(getInitialSortBy);
  const [sortDescending, setSortDescending] = useState(getInitialSortDesc);
  const [filenameFilter, setFilenameFilter] = useState(getInitialFilenameFilter);
  const [statusFilter, setStatusFilter] = useState(getInitialStatusFilter);
  const [contentTypeFilter, setContentTypeFilter] = useState(getInitialContentTypeFilter);
  const [filenameInput, setFilenameInput] = useState(getInitialFilenameFilter);

  const [pendingDelete, setPendingDelete] = useState<DocumentSummary | null>(null);

  const { data, isLoading, error } = useKnowledgeBaseDocumentsQuery({
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
    filenameFilter: filenameFilter || undefined,
    statusFilter: statusFilter || undefined,
    contentTypeFilter: contentTypeFilter || undefined,
  });

  const { data: contentTypesData } = useKnowledgeBaseContentTypesQuery();
  const deleteDocument = useDeleteKnowledgeBaseDocumentMutation();

  // Sync state → URL
  useEffect(() => {
    const params = new URLSearchParams(searchParams);

    if (pageNumber !== DEFAULT_PAGE) params.set('kbPage', pageNumber.toString());
    else params.delete('kbPage');

    if (pageSize !== DEFAULT_PAGE_SIZE) params.set('kbPageSize', pageSize.toString());
    else params.delete('kbPageSize');

    if (sortBy !== DEFAULT_SORT_BY) params.set('kbSortBy', sortBy);
    else params.delete('kbSortBy');

    if (sortDescending !== DEFAULT_SORT_DESC) params.set('kbSortDesc', 'false');
    else params.delete('kbSortDesc');

    if (filenameFilter) params.set('kbFilename', filenameFilter);
    else params.delete('kbFilename');

    if (statusFilter) params.set('kbStatus', statusFilter);
    else params.delete('kbStatus');

    if (contentTypeFilter) params.set('kbContentType', contentTypeFilter);
    else params.delete('kbContentType');

    setSearchParams(params, { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
    filenameFilter,
    statusFilter,
    contentTypeFilter,
    setSearchParams,
  ]);

  // URL → state (back/forward navigation)
  useEffect(() => {
    const newPage = Math.max(1, parseInt(searchParams.get('kbPage') || '1', 10));
    const newPageSizeRaw = parseInt(searchParams.get('kbPageSize') || '20', 10);
    const newPageSize = [10, 20, 50].includes(newPageSizeRaw) ? newPageSizeRaw : DEFAULT_PAGE_SIZE;
    const newSortBy = searchParams.get('kbSortBy') || DEFAULT_SORT_BY;
    const newSortDesc = searchParams.get('kbSortDesc') !== 'false';
    const newFilename = searchParams.get('kbFilename') || '';
    const newStatus = searchParams.get('kbStatus') || '';
    const newContentType = searchParams.get('kbContentType') || '';

    if (
      newPage !== pageNumber ||
      newPageSize !== pageSize ||
      newSortBy !== sortBy ||
      newSortDesc !== sortDescending ||
      newFilename !== filenameFilter ||
      newStatus !== statusFilter ||
      newContentType !== contentTypeFilter
    ) {
      setPageNumber(newPage);
      setPageSize(newPageSize);
      setSortBy(newSortBy);
      setSortDescending(newSortDesc);
      setFilenameFilter(newFilename);
      setFilenameInput(newFilename);
      setStatusFilter(newStatus);
      setContentTypeFilter(newContentType);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  const handleApplyFilters = () => {
    setFilenameFilter(filenameInput);
    setPageNumber(1);
  };

  const handleClearFilters = () => {
    setFilenameInput('');
    setFilenameFilter('');
    setStatusFilter('');
    setContentTypeFilter('');
    setSortBy(DEFAULT_SORT_BY);
    setSortDescending(DEFAULT_SORT_DESC);
    setPageNumber(1);
  };

  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
    setPageNumber(1);
  };

  const handleDeleteConfirm = async () => {
    if (!pendingDelete) return;
    try {
      await deleteDocument.mutateAsync(pendingDelete.id);
    } finally {
      setPendingDelete(null);
    }
  };

  const totalPages = data?.totalPages ?? 0;
  const documents = data?.documents ?? [];

  const SortableHeader: React.FC<{ column: string; children: React.ReactNode }> = ({
    column,
    children,
  }) => {
    const isActive = sortBy === column;
    return (
      <th
        className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
        onClick={() => handleSort(column)}
      >
        <div className="flex items-center space-x-1">
          <span>{children}</span>
          <div className="flex flex-col">
            <ChevronUp
              className={`h-3 w-3 ${isActive && !sortDescending ? 'text-indigo-600' : 'text-gray-300'}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isActive && sortDescending ? 'text-indigo-600' : 'text-gray-300'}`}
            />
          </div>
        </div>
      </th>
    );
  };

  if (isLoading) {
    return (
      <div className="space-y-2 animate-pulse">
        {[1, 2, 3].map((i) => (
          <div key={i} className="h-10 bg-gray-100 rounded" />
        ))}
      </div>
    );
  }

  if (error) {
    return <div className="text-red-600 text-sm">Nepodařilo se načíst dokumenty.</div>;
  }

  return (
    <>
      {/* Filter bar */}
      <div className="bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  value={filenameInput}
                  onChange={(e) => setFilenameInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleApplyFilters()}
                  placeholder="Název souboru..."
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <select
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPageNumber(1);
                }}
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="">Vše (stav)</option>
                <option value="indexed">Indexováno</option>
                <option value="processing">Zpracovává se</option>
                <option value="failed">Selhalo</option>
              </select>
            </div>

            <div className="flex-1 max-w-xs">
              <select
                value={contentTypeFilter}
                onChange={(e) => {
                  setContentTypeFilter(e.target.value);
                  setPageNumber(1);
                }}
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="">Vše (typ)</option>
                {(contentTypesData?.contentTypes ?? []).map((ct) => (
                  <option key={ct} value={ct}>
                    {ct}
                  </option>
                ))}
              </select>
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

      {documents.length === 0 ? (
        <div className="text-gray-500 text-sm text-center py-8">
          Žádné dokumenty neodpovídají zadaným filtrům.
        </div>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <SortableHeader column="Filename">Soubor</SortableHeader>
                  <SortableHeader column="Status">Stav</SortableHeader>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ</th>
                  <SortableHeader column="CreatedAt">Vytvořeno</SortableHeader>
                  <SortableHeader column="IndexedAt">Indexováno</SortableHeader>
                  {canDelete && <th className="px-6 py-3" />}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {documents.map((doc) => (
                  <tr key={doc.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{doc.filename}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      <StatusBadge status={doc.status} />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{doc.contentType}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {new Date(doc.createdAt).toLocaleDateString('cs-CZ')}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {doc.indexedAt
                        ? new Date(doc.indexedAt).toLocaleDateString('cs-CZ')
                        : '–'}
                    </td>
                    {canDelete && (
                      <td className="px-6 py-4 whitespace-nowrap text-right">
                        <button
                          onClick={() => setPendingDelete(doc)}
                          title="Smazat dokument"
                          className="text-gray-400 hover:text-red-600 transition-colors"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination footer */}
          <div className="bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
            <div className="flex-1 flex justify-between sm:hidden">
              <button
                onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                disabled={pageNumber <= 1}
                className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Předchozí
              </button>
              <button
                onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
                disabled={pageNumber >= totalPages}
                className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Další
              </button>
            </div>
            <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
              <div className="flex items-center space-x-3">
                <p className="text-xs text-gray-600">
                  {Math.min((pageNumber - 1) * pageSize + 1, data?.totalCount ?? 0)}–
                  {Math.min(pageNumber * pageSize, data?.totalCount ?? 0)} z {data?.totalCount ?? 0}
                </p>
                <div className="flex items-center space-x-1">
                  <span className="text-xs text-gray-600">Zobrazit:</span>
                  <select
                    value={pageSize}
                    onChange={(e) => {
                      setPageSize(parseInt(e.target.value, 10));
                      setPageNumber(1);
                    }}
                    className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                  >
                    <option value={10}>10</option>
                    <option value={20}>20</option>
                    <option value={50}>50</option>
                  </select>
                </div>
              </div>
              <div>
                <nav
                  className="relative z-0 inline-flex rounded shadow-sm -space-x-px"
                  aria-label="Pagination"
                >
                  <button
                    onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                    disabled={pageNumber <= 1}
                    className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <ChevronLeft className="h-3 w-3" />
                  </button>

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
                        onClick={() => setPageNumber(pageNum)}
                        className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                          pageNum === pageNumber
                            ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                            : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}

                  <button
                    onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
                    disabled={pageNumber >= totalPages}
                    className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <ChevronRight className="h-3 w-3" />
                  </button>
                </nav>
              </div>
            </div>
          </div>
        </>
      )}

      {pendingDelete && (
        <ConfirmDeleteDialog
          document={pendingDelete}
          onConfirm={handleDeleteConfirm}
          onCancel={() => setPendingDelete(null)}
        />
      )}
    </>
  );
};

export default KnowledgeBaseDocumentsTab;
