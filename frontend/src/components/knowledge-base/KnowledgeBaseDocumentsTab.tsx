import React, { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Trash2, ChevronUp, ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react';
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
        className="px-4 py-2 text-left font-medium text-gray-500 cursor-pointer hover:bg-gray-100 select-none"
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
      <div className="flex flex-wrap gap-2 mb-3 items-center">
        <input
          type="text"
          value={filenameInput}
          onChange={(e) => setFilenameInput(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleApplyFilters()}
          placeholder="Název souboru…"
          className="border border-gray-300 rounded px-3 py-1.5 text-sm focus:outline-none focus:ring-1 focus:ring-indigo-400 w-48"
        />

        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value);
            setPageNumber(1);
          }}
          className="border border-gray-300 rounded px-3 py-1.5 text-sm focus:outline-none focus:ring-1 focus:ring-indigo-400"
        >
          <option value="">Vše (stav)</option>
          <option value="indexed">Indexováno</option>
          <option value="processing">Zpracovává se</option>
          <option value="failed">Selhalo</option>
        </select>

        <select
          value={contentTypeFilter}
          onChange={(e) => {
            setContentTypeFilter(e.target.value);
            setPageNumber(1);
          }}
          className="border border-gray-300 rounded px-3 py-1.5 text-sm focus:outline-none focus:ring-1 focus:ring-indigo-400"
        >
          <option value="">Vše (typ)</option>
          {(contentTypesData?.contentTypes ?? []).map((ct) => (
            <option key={ct} value={ct}>
              {ct}
            </option>
          ))}
        </select>

        <button
          onClick={handleApplyFilters}
          className="px-3 py-1.5 text-sm rounded bg-indigo-600 text-white hover:bg-indigo-700"
        >
          Použít
        </button>
        <button
          onClick={handleClearFilters}
          className="px-3 py-1.5 text-sm rounded border border-gray-300 hover:bg-gray-50"
        >
          Vymazat
        </button>
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
                  <th className="px-4 py-2 text-left font-medium text-gray-500">Typ</th>
                  <SortableHeader column="CreatedAt">Vytvořeno</SortableHeader>
                  <SortableHeader column="IndexedAt">Indexováno</SortableHeader>
                  {canDelete && <th className="px-4 py-2" />}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {documents.map((doc) => (
                  <tr key={doc.id} className="hover:bg-gray-50">
                    <td className="px-4 py-2 font-medium">{doc.filename}</td>
                    <td className="px-4 py-2">
                      <StatusBadge status={doc.status} />
                    </td>
                    <td className="px-4 py-2 text-gray-500">{doc.contentType}</td>
                    <td className="px-4 py-2 text-gray-500">
                      {new Date(doc.createdAt).toLocaleDateString('cs-CZ')}
                    </td>
                    <td className="px-4 py-2 text-gray-500">
                      {doc.indexedAt
                        ? new Date(doc.indexedAt).toLocaleDateString('cs-CZ')
                        : '–'}
                    </td>
                    {canDelete && (
                      <td className="px-4 py-2 text-right">
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
          <div className="flex items-center justify-between mt-3 text-sm">
            <div className="flex items-center gap-2">
              <span className="text-gray-500">Zobrazit:</span>
              <select
                value={pageSize}
                onChange={(e) => {
                  setPageSize(parseInt(e.target.value, 10));
                  setPageNumber(1);
                }}
                className="border border-gray-300 rounded px-2 py-1 text-sm focus:outline-none focus:ring-1 focus:ring-indigo-400"
              >
                <option value={10}>10</option>
                <option value={20}>20</option>
                <option value={50}>50</option>
              </select>
              <span className="text-gray-500">celkem {data?.totalCount ?? 0} záznamů</span>
            </div>

            <div className="flex items-center gap-1">
              <button
                onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                disabled={pageNumber <= 1}
                className="p-1 rounded hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>

              <span className="px-2 text-gray-700">
                {pageNumber} / {totalPages}
              </span>

              <button
                onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
                disabled={pageNumber >= totalPages}
                className="p-1 rounded hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
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
