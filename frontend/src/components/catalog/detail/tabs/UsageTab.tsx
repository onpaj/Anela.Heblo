import React from 'react';
import { ArrowRight, Loader2, AlertCircle } from 'lucide-react';
import { useProductUsage } from '../../../../api/hooks/useProductUsage';

interface UsageTabProps {
  productCode: string;
}

const UsageTab: React.FC<UsageTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductUsage(productCode);
  const manufactureTemplates = data?.manufactureTemplates || [];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání použití produktu...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání použití: {(error as any).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <ArrowRight className="h-5 w-5 mr-2 text-gray-500" />
          Použití v produktech ({manufactureTemplates.length})
        </h3>
      </div>

      {/* Usage grid */}
      {manufactureTemplates.length === 0 ? (
        <div className="text-center py-12 bg-gray-50 rounded-lg">
          <ArrowRight className="h-12 w-12 mx-auto mb-3 text-gray-300" />
          <p className="text-gray-500 mb-2">Tento produkt se nikde nepoužívá</p>
          <p className="text-sm text-gray-400">Materiál nebo polotovar není součástí žádné výrobní šablony</p>
        </div>
      ) : (
        <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
          <div className="h-96 overflow-y-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
                <tr>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">Kód produktu</th>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">Název produktu</th>
                  <th className="text-right py-3 px-4 font-medium text-gray-700">Množství</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {manufactureTemplates.map((template) => (
                  <tr key={template.templateId} className="hover:bg-gray-50">
                    <td className="py-3 px-4 text-gray-900 font-medium">
                      {template.productCode}
                    </td>
                    <td className="py-3 px-4 text-gray-900" title={template.productName}>
                      {template.productName}
                    </td>
                    <td className="py-3 px-4 text-right text-gray-900 font-medium">
                      {template.amount?.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 4 }) || '0'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
};

export default UsageTab;