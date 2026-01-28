import React, { useState } from 'react';
import { Loader2, AlertCircle, Beaker } from 'lucide-react';
import { useProductComposition } from '../../../../api/hooks/useCatalog';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';

interface CompositionTabProps {
  productCode: string;
}

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);
  const [sortConfig, setSortConfig] = useState<{
    key: keyof IngredientDto;
    direction: 'asc' | 'desc';
  } | null>(null);

  const ingredients = data?.ingredients || [];

  const sortedIngredients = React.useMemo(() => {
    if (!sortConfig) return ingredients;

    const sorted = [...ingredients].sort((a, b) => {
      const aValue = a[sortConfig.key];
      const bValue = b[sortConfig.key];

      if (typeof aValue === 'number' && typeof bValue === 'number') {
        return sortConfig.direction === 'asc' ? aValue - bValue : bValue - aValue;
      }

      const aString = String(aValue);
      const bString = String(bValue);

      return sortConfig.direction === 'asc'
        ? aString.localeCompare(bString, 'cs')
        : bString.localeCompare(aString, 'cs');
    });

    return sorted;
  }, [ingredients, sortConfig]);

  const handleSort = (key: keyof IngredientDto) => {
    setSortConfig((current) => {
      if (!current || current.key !== key) {
        return { key, direction: 'asc' };
      }
      if (current.direction === 'asc') {
        return { key, direction: 'desc' };
      }
      return null;
    });
  };

  const getSortIcon = (key: keyof IngredientDto) => {
    if (!sortConfig || sortConfig.key !== key) return null;
    return sortConfig.direction === 'asc' ? ' ↑' : ' ↓';
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání složení...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání složení: {(error as any).message}</div>
        </div>
      </div>
    );
  }

  if (ingredients.length === 0) {
    return (
      <div className="text-center py-12 bg-gray-50 rounded-lg">
        <Beaker className="h-12 w-12 mx-auto mb-3 text-gray-300" />
        <p className="text-gray-500 mb-2">Tento produkt nemá definované složení</p>
        <p className="text-sm text-gray-400">
          Výrobní šablona pro tento produkt neexistuje
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <Beaker className="h-5 w-5 mr-2 text-gray-500" />
          Složení ({sortedIngredients.length} ingrediencí)
        </h3>
      </div>

      {/* Ingredient table */}
      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <div className="h-96 overflow-y-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
              <tr>
                <th
                  className="text-left py-3 px-4 font-medium text-gray-700 cursor-pointer hover:bg-gray-100"
                  onClick={() => handleSort('productName')}
                >
                  Název{getSortIcon('productName')}
                </th>
                <th
                  className="text-left py-3 px-4 font-medium text-gray-700 cursor-pointer hover:bg-gray-100"
                  onClick={() => handleSort('productCode')}
                >
                  Kód{getSortIcon('productCode')}
                </th>
                <th
                  className="text-right py-3 px-4 font-medium text-gray-700 cursor-pointer hover:bg-gray-100"
                  onClick={() => handleSort('amount')}
                >
                  Množství{getSortIcon('amount')}
                </th>
                <th className="text-left py-3 px-4 font-medium text-gray-700">
                  Jednotka
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {sortedIngredients.map((ingredient, index) => (
                <tr key={index} className="hover:bg-gray-50">
                  <td className="py-3 px-4 text-gray-900">{ingredient.productName}</td>
                  <td className="py-3 px-4 text-gray-900 font-medium">
                    {ingredient.productCode}
                  </td>
                  <td className="py-3 px-4 text-right text-gray-900 font-medium">
                    {ingredient.amount.toLocaleString('cs-CZ', {
                      minimumFractionDigits: 2,
                      maximumFractionDigits: 4,
                    })}
                  </td>
                  <td className="py-3 px-4 text-gray-900">{ingredient.unit}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

export default CompositionTab;
