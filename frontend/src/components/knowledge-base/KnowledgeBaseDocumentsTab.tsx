import React, { useState } from 'react';
import { Trash2 } from 'lucide-react';
import {
  useKnowledgeBaseDocumentsQuery,
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

const KnowledgeBaseDocumentsTab: React.FC<Props> = ({ canDelete }) => {
  const { data, isLoading, error } = useKnowledgeBaseDocumentsQuery();
  const deleteDocument = useDeleteKnowledgeBaseDocumentMutation();
  const [pendingDelete, setPendingDelete] = useState<DocumentSummary | null>(null);

  const handleDeleteConfirm = async () => {
    if (!pendingDelete) return;
    try {
      await deleteDocument.mutateAsync(pendingDelete.id);
    } finally {
      setPendingDelete(null);
    }
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
    return (
      <div className="text-red-600 text-sm">Nepodařilo se načíst dokumenty.</div>
    );
  }

  const documents = data?.documents ?? [];

  if (documents.length === 0) {
    return (
      <div className="text-gray-500 text-sm text-center py-8">
        Žádné dokumenty nejsou indexovány. Nahrajte soubory do OneDrive složky Inbox.
      </div>
    );
  }

  return (
    <>
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200 text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-2 text-left font-medium text-gray-500">Soubor</th>
              <th className="px-4 py-2 text-left font-medium text-gray-500">Stav</th>
              <th className="px-4 py-2 text-left font-medium text-gray-500">Typ</th>
              <th className="px-4 py-2 text-left font-medium text-gray-500">Vytvořeno</th>
              <th className="px-4 py-2 text-left font-medium text-gray-500">Indexováno</th>
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
