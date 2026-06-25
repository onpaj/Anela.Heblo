import { useState } from 'react';
import { RefreshCw, Upload } from 'lucide-react';
import { usePifDocuments } from '../../../../api/hooks/useCatalogDocuments';
import DocumentList from './shared/DocumentList';
import FolderStatusBanner from './shared/FolderStatusBanner';
import PifUploadDialog from './shared/PifUploadDialog';

interface PifDocumentsTabProps {
  productCode: string;
}

export default function PifDocumentsTab({ productCode }: PifDocumentsTabProps) {
  const [isUploadOpen, setIsUploadOpen] = useState(false);
  const { data, isLoading, error, refetch } = usePifDocuments(productCode);

  if (error) {
    return (
      <div className="py-6 text-sm text-red-600 dark:text-red-400">
        Chyba při načítání PIF dokumentů. Zkuste obnovit stránku.
      </div>
    );
  }

  const folderStatus = data?.folderStatus ?? 'NotFound';

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium text-gray-900 dark:text-graphite-text">PIF</h3>
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
              Nahrát PIF
            </button>
          )}
        </div>
      </div>

      <FolderStatusBanner
        status={folderStatus}
        expectedPrefix={data?.expectedPrefix ?? ''}
        basePath={data?.basePath ?? ''}
      />

      {folderStatus === 'Found' && (
        <DocumentList
          files={data?.files ?? []}
          isLoading={isLoading}
          onUploadClick={() => setIsUploadOpen(true)}
        />
      )}

      <PifUploadDialog
        isOpen={isUploadOpen}
        productCode={productCode}
        onClose={() => setIsUploadOpen(false)}
        onSuccess={() => refetch()}
      />
    </div>
  );
}
