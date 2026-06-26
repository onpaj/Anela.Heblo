import { useState, useRef } from 'react';
import { useMaterialDocumentTypes, useUploadMaterialDocument } from '../../../../../api/hooks/useCatalogDocuments';

interface MaterialUploadDialogProps {
  isOpen: boolean;
  productCode: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export default function MaterialUploadDialog({ isOpen, productCode, onClose, onSuccess }: MaterialUploadDialogProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [documentTypeCode, setDocumentTypeCode] = useState('');
  const [lot, setLot] = useState('');
  const [commonName, setCommonName] = useState('');
  const [uploadAsIs, setUploadAsIs] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const { data: typesData } = useMaterialDocumentTypes();
  const uploadMutation = useUploadMaterialDocument();

  const documentTypes = typesData?.documentTypes ?? [];
  const selectedType = documentTypes.find((t) => t.code === documentTypeCode);

  if (!isOpen) return null;

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setSelectedFile(file);
    if (file && !commonName) {
      const nameWithoutExt = file.name.replace(/\.[^.]+$/, '');
      setCommonName(nameWithoutExt);
    }
  }

  function resetForm() {
    setSelectedFile(null);
    setDocumentTypeCode('');
    setLot('');
    setCommonName('');
    setUploadAsIs(false);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedFile) return;
    uploadMutation.mutate(
      { productCode, file: selectedFile, documentTypeCode, lot, commonName, uploadAsIs },
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
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6 dark:bg-graphite-surface dark:shadow-soft-dark">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-graphite-text mb-4">Nahrát dokument</h2>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Soubor</label>
            <input
              ref={fileInputRef}
              type="file"
              onChange={handleFileChange}
              className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-indigo-50 file:text-indigo-700 hover:file:bg-indigo-100 dark:text-graphite-muted dark:file:bg-graphite-accent/10 dark:file:text-graphite-accent"
            />
          </div>

          <div className="flex items-center gap-2">
            <input
              id="upload-as-is"
              type="checkbox"
              checked={uploadAsIs}
              onChange={(e) => setUploadAsIs(e.target.checked)}
              className="h-4 w-4 rounded border-gray-300 text-indigo-600 dark:border-graphite-border"
            />
            <label htmlFor="upload-as-is" className="text-sm text-gray-700 dark:text-graphite-muted">
              Nahrát beze změny názvu
            </label>
          </div>

          {!uploadAsIs && (
            <>
              <div>
                <label htmlFor="doc-type" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
                  Typ dokumentu
                </label>
                <select
                  id="doc-type"
                  value={documentTypeCode}
                  onChange={(e) => { setDocumentTypeCode(e.target.value); setLot(''); }}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm text-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                >
                  <option value="">— Vyberte typ —</option>
                  {documentTypes.map((t) => (
                    <option key={t.code} value={t.code}>{t.label}</option>
                  ))}
                </select>
              </div>

              {selectedType?.lotRequired && (
                <div>
                  <label htmlFor="lot" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
                    Šarže
                  </label>
                  <input
                    id="lot"
                    type="text"
                    value={lot}
                    onChange={(e) => setLot(e.target.value)}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm text-sm dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                    placeholder="např. 2024-001"
                  />
                </div>
              )}

              <div>
                <label htmlFor="common-name" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
                  Název
                </label>
                <input
                  id="common-name"
                  type="text"
                  value={commonName}
                  onChange={(e) => setCommonName(e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm text-sm dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                />
              </div>
            </>
          )}

          {(uploadMutation.isError || uploadMutation.data?.success === false) && (
            <p className="text-sm text-red-600 dark:text-red-400">Nahrání selhalo. Zkuste to znovu.</p>
          )}

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 dark:text-graphite-muted dark:border-graphite-border dark:hover:bg-white/5"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={!selectedFile || uploadMutation.isPending || (!uploadAsIs && (!documentTypeCode || (selectedType?.lotRequired && !lot.trim())))}
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
