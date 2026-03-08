import React, { useState } from 'react';
import { Trash2 } from 'lucide-react';
import { useKnowledgeBaseDocumentsQuery, useDeleteKnowledgeBaseDocumentMutation } from '../../api/hooks/useKnowledgeBase';

const STATUS_COLORS: Record<string, string> = {
  indexed: 'bg-green-100 text-green-800',
  processing: 'bg-yellow-100 text-yellow-800',
  failed: 'bg-red-100 text-red-800',
};

const STATUS_LABELS: Record<string, string> = {
  indexed: 'Indexováno',
  processing: 'Zpracovává se',
  failed: 'Chyba',
};

const KnowledgeBaseDocumentsTab: React.FC = () => {
  const { data: documents, isLoading, isError, refetch } = useKnowledgeBaseDocumentsQuery();
  const deleteMutation = useDeleteKnowledgeBaseDocumentMutation();
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  if (isLoading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => (
          <div key={i} className="h-12 bg-gray-100 rounded animate-pulse" />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div className="text-center py-12">
        <p className="text-red-600 mb-4">Nepodařilo se načíst dokumenty.</p>
        <button onClick={() => refetch()} className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700">
          Zkusit znovu
        </button>
      </div>
    );
  }

  if (!documents || documents.length === 0) {
    return (
      <div className="text-center py-12 text-gray-500">
        <p>Žádné dokumenty nejsou indexovány.</p>
        <p className="text-sm mt-1">Dokumenty se načítají z OneDrive každých 15 minut.</p>
      </div>
    );
  }

  const handleDelete = async (id: string) => {
    await deleteMutation.mutateAsync(id);
    setConfirmDeleteId(null);
  };

  return (
    <>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200 text-left text-gray-500">
            <th className="pb-2 font-medium">Soubor</th>
            <th className="pb-2 font-medium">Typ</th>
            <th className="pb-2 font-medium">Status</th>
            <th className="pb-2 font-medium">Indexováno</th>
            <th className="pb-2" />
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {documents.map((doc) => (
            <tr key={doc.id} className="hover:bg-gray-50">
              <td className="py-3 font-medium text-gray-900">{doc.filename}</td>
              <td className="py-3 text-gray-500">{doc.contentType}</td>
              <td className="py-3">
                <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_COLORS[doc.status] ?? 'bg-gray-100 text-gray-800'}`}>
                  {STATUS_LABELS[doc.status] ?? doc.status}
                </span>
              </td>
              <td className="py-3 text-gray-500">
                {doc.indexedAt
                  ? new Date(doc.indexedAt).toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' })
                  : '—'}
              </td>
              <td className="py-3 text-right">
                <button
                  onClick={() => setConfirmDeleteId(doc.id)}
                  className="text-gray-400 hover:text-red-600 transition-colors"
                  title="Smazat dokument"
                >
                  <Trash2 size={16} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Confirm delete dialog */}
      {confirmDeleteId && (
        <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Smazat dokument?</h3>
            <p className="text-sm text-gray-600 mb-6">
              Dokument a všechny jeho indexované části budou trvale odstraněny.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setConfirmDeleteId(null)}
                className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded hover:bg-gray-50"
              >
                Zrušit
              </button>
              <button
                onClick={() => handleDelete(confirmDeleteId)}
                disabled={deleteMutation.isPending}
                className="px-4 py-2 text-sm bg-red-600 text-white rounded hover:bg-red-700 disabled:opacity-50"
              >
                {deleteMutation.isPending ? 'Mazání...' : 'Smazat'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default KnowledgeBaseDocumentsTab;
