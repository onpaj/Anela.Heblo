import { useState, useRef } from 'react';
import { useUploadPifDocument } from '../../../../../api/hooks/useCatalogDocuments';

interface PifUploadDialogProps {
  isOpen: boolean;
  productCode: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export default function PifUploadDialog({ isOpen, productCode, onClose, onSuccess }: PifUploadDialogProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const uploadMutation = useUploadPifDocument();

  if (!isOpen) return null;

  function resetForm() {
    setSelectedFile(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedFile) return;
    uploadMutation.mutate(
      { productCode, file: selectedFile },
      {
        onSuccess: (data) => {
          if (data.success) {
            resetForm();
            onSuccess?.();
            onClose();
          }
        },
      }
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Nahrát PIF</h2>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Soubor</label>
            <input
              ref={fileInputRef}
              type="file"
              onChange={(e) => setSelectedFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-indigo-50 file:text-indigo-700 hover:file:bg-indigo-100"
            />
          </div>

          {uploadMutation.isError && (
            <p className="text-sm text-red-600">Nahrání selhalo. Zkuste to znovu.</p>
          )}

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={!selectedFile || uploadMutation.isPending}
              className="px-4 py-2 text-sm text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50"
            >
              {uploadMutation.isPending ? 'Nahrávám…' : 'Nahrát'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
