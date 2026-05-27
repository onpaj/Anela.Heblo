import type { FolderStatus } from '../../../../../api/hooks/useCatalogDocuments';

interface FolderStatusBannerProps {
  status: FolderStatus;
  expectedPrefix: string;
  basePath: string;
}

export default function FolderStatusBanner({ status, expectedPrefix, basePath }: FolderStatusBannerProps) {
  if (status === 'Found') return null;

  if (status === 'MultipleMatches') {
    return (
      <div className="rounded-md bg-yellow-50 border border-yellow-200 px-4 py-3 text-sm text-yellow-800">
        {`Nalezeno více složek odpovídajících prefixu ${expectedPrefix}. Upravte strukturu složek v SharePointu a obnovte stránku.`}
      </div>
    );
  }

  return (
    <div className="rounded-md bg-gray-50 border border-gray-200 px-4 py-3 text-sm text-gray-600">
      {`Složka pro ${expectedPrefix} nebyla nalezena pod ${basePath}. Vytvořte ji v SharePointu a obnovte stránku.`}
    </div>
  );
}
