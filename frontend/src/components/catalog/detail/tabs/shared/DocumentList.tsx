import type { CatalogDocumentDto } from '../../../../../api/hooks/useCatalogDocuments';

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

interface DocumentListProps {
  files: CatalogDocumentDto[];
  isLoading: boolean;
  onUploadClick?: () => void;
}

export default function DocumentList({ files, isLoading, onUploadClick }: DocumentListProps) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-8 text-gray-500 text-sm">
        Načítání…
      </div>
    );
  }

  if (files.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-8 gap-3 text-gray-500 text-sm">
        <span>Žádné dokumenty</span>
        {onUploadClick && (
          <button
            onClick={onUploadClick}
            className="text-indigo-600 hover:text-indigo-800 text-sm font-medium"
          >
            Nahrát soubor
          </button>
        )}
      </div>
    );
  }

  return (
    <ul className="divide-y divide-gray-100">
      {files.map((file) => (
        <li key={file.webUrl} className="flex items-center justify-between py-3 px-1 hover:bg-gray-50 rounded">
          <a
            href={file.webUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-sm text-indigo-600 hover:text-indigo-800 hover:underline truncate max-w-xs"
            title={file.name}
          >
            {file.name}
          </a>
          <div className="flex items-center gap-4 text-xs text-gray-500 ml-4 shrink-0">
            <span>{formatFileSize(file.sizeBytes)}</span>
            <span>{new Date(file.modifiedAt).toLocaleDateString('cs-CZ')}</span>
          </div>
        </li>
      ))}
    </ul>
  );
}
