import React from 'react';
import { Loader2, AlertCircle, Beaker } from 'lucide-react';
import { useProductComposition } from '../../../../api/hooks/useCatalog';

interface CompositionTabProps {
  productCode: string;
}

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);

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

  const ingredients = data?.ingredients || [];

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

  return <div>Ingredient table placeholder</div>;
};

export default CompositionTab;
