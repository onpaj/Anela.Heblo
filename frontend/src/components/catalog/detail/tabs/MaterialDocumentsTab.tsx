import { useState } from 'react';
import { RefreshCw, Upload } from 'lucide-react';
import { useMaterialDocuments } from '../../../../api/hooks/useCatalogDocuments';
import DocumentList from './shared/DocumentList';
import FolderStatusBanner from './shared/FolderStatusBanner';
import MaterialUploadDialog from './shared/MaterialUploadDialog';

interface MaterialDocumentsTabProps {
  productCode: string;
}

export default function MaterialDocumentsTab({ productCode }: MaterialDocumentsTabProps) {
  const [isUploadOpen, setIsUploadOpen] = useState(false);
  const { data, isLoading, error, refetch } = useMaterialDocuments(productCode);

  if (error) {
    return (
      <div className="py-6 text-sm text-red-600 dark:text-red-400">
        Chyba při načítání dokumentů. Zkuste obnovit stránku.
      </div>
    );
  }

  const folderStatus = data?.folderStatus ?? 'NotFound';

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium text-gray-900 dark:text-graphite-text">Dokumenty</h3>
        <div className="flex items-center gap-2">
          <button
            onClick={() => refetch()}
            className="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded dark:text-graphite-muted dark:hover:text-graphite-text dark:hover:bg-white/5"
            title="Obnovit"
          >
            <RefreshCw className="h-4 w-4" />
          </button>
          {folderStatus === 'Found' && (
            <button
              onClick={() => setIsUploadOpen(true)}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md"
            >
              <Upload className="h-3.5 w-3.5" />
              Nahrát soubor
            </button>
          )}
        </div>
      </div>

      <FolderStatusBanner
        status={folderStatus}
        expectedPrefix={data?.expectedPrefix ?? `${productCode}__`}
        basePath={data?.basePath ?? ''}
      />

      {folderStatus === 'Found' && (
        <DocumentList
          files={data?.files ?? []}
          isLoading={isLoading}
          onUploadClick={() => setIsUploadOpen(true)}
        />
      )}

      <MaterialUploadDialog
        isOpen={isUploadOpen}
        productCode={productCode}
        onClose={() => setIsUploadOpen(false)}
        onSuccess={() => refetch()}
      />
    </div>
  );
}
