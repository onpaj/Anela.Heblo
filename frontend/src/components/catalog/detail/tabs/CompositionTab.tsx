import React, { useEffect, useState } from 'react';
import { Loader2, AlertCircle, Beaker, Pencil, Save, X } from 'lucide-react';
import {
  DndContext,
  closestCenter,
  PointerSensor,
  useSensor,
  useSensors,
  DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  arrayMove,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { useProductComposition } from '../../../../api/hooks/useCatalog';
import { useUpdateProductCompositionOrder } from '../../../../api/hooks/useUpdateProductCompositionOrder';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';
import { CompositionTabRow } from './CompositionTabRow';

interface CompositionTabProps {
  productCode: string;
}

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);
  const updateOrder = useUpdateProductCompositionOrder();

  const [isEditMode, setIsEditMode] = useState(false);
  const [draftOrder, setDraftOrder] = useState<IngredientDto[] | null>(null);
  const [sortConfig, setSortConfig] = useState<{
    key: keyof IngredientDto;
    direction: 'asc' | 'desc';
  } | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const ingredients = React.useMemo(() => data?.ingredients ?? [], [data?.ingredients]);

  // When data refreshes while not editing, clear any stale draft
  useEffect(() => {
    if (!isEditMode) {
      setDraftOrder(null);
    }
  }, [ingredients, isEditMode]);

  const sortedIngredients = React.useMemo(() => {
    if (isEditMode) {
      return draftOrder ?? ingredients;
    }
    if (!sortConfig) return ingredients; // server already sorted by custom order

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
  }, [ingredients, sortConfig, isEditMode, draftOrder]);

  const handleSort = (key: keyof IngredientDto) => {
    if (isEditMode) return;
    setSortConfig((current) => {
      if (!current || current.key !== key) return { key, direction: 'asc' };
      if (current.direction === 'asc') return { key, direction: 'desc' };
      return null;
    });
  };

  const getSortIcon = (key: keyof IngredientDto) => {
    if (!sortConfig || sortConfig.key !== key) return null;
    return sortConfig.direction === 'asc' ? ' ↑' : ' ↓';
  };

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    setDraftOrder((current) => {
      const list = current ?? ingredients;
      const oldIndex = list.findIndex((i) => i.productCode === active.id);
      const newIndex = list.findIndex((i) => i.productCode === over.id);
      if (oldIndex < 0 || newIndex < 0) return current;
      return arrayMove(list, oldIndex, newIndex);
    });
  };

  const enterEditMode = () => {
    setDraftOrder([...ingredients]);
    setSortConfig(null);
    setSaveError(null);
    setIsEditMode(true);
  };

  const cancelEdit = () => {
    setDraftOrder(null);
    setSaveError(null);
    setIsEditMode(false);
  };

  const saveOrder = async () => {
    if (!draftOrder) {
      setIsEditMode(false);
      return;
    }
    setSaveError(null);
    try {
      await updateOrder.mutateAsync({
        productCode,
        order: draftOrder.map((ing, idx) => ({
          ingredientProductCode: ing.productCode,
          sortOrder: idx + 1,
        })),
      });
      setIsEditMode(false);
      setDraftOrder(null);
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Uložení se nezdařilo');
    }
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
        <p className="text-sm text-gray-400">Výrobní šablona pro tento produkt neexistuje</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <Beaker className="h-5 w-5 mr-2 text-gray-500" />
          Složení ({sortedIngredients.length} ingrediencí)
        </h3>
        <div className="flex items-center space-x-2">
          {!isEditMode && (
            <button
              type="button"
              onClick={enterEditMode}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50"
            >
              <Pencil className="h-4 w-4 mr-1.5" />
              Upravit pořadí
            </button>
          )}
          {isEditMode && (
            <>
              <button
                type="button"
                onClick={saveOrder}
                disabled={updateOrder.isPending}
                className="inline-flex items-center px-3 py-1.5 text-sm rounded-md text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-60"
              >
                <Save className="h-4 w-4 mr-1.5" />
                Uložit
              </button>
              <button
                type="button"
                onClick={cancelEdit}
                disabled={updateOrder.isPending}
                className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50"
              >
                <X className="h-4 w-4 mr-1.5" />
                Zrušit
              </button>
            </>
          )}
        </div>
      </div>

      {saveError && (
        <div className="flex items-center space-x-2 px-3 py-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded mb-4">
          <AlertCircle className="h-4 w-4" />
          <span>{saveError}</span>
        </div>
      )}

      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={handleDragEnd}
      >
        <div className="flex-1 min-h-0 bg-white rounded-lg border border-gray-200 overflow-hidden">
          <div className="h-full overflow-y-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
                <tr>
                  {isEditMode && <th className="w-8 py-3 px-2" />}
                  <th className="text-right py-3 px-4 font-medium text-gray-700 w-16">#</th>
                  <th
                    className={`text-left py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                    onClick={() => handleSort('productName')}
                  >
                    Název{getSortIcon('productName')}
                  </th>
                  <th
                    className={`text-left py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                    onClick={() => handleSort('productCode')}
                  >
                    Kód{getSortIcon('productCode')}
                  </th>
                  <th
                    className={`text-right py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                    onClick={() => handleSort('amount')}
                  >
                    Množství{getSortIcon('amount')}
                  </th>
                </tr>
              </thead>
              <SortableContext
                items={sortedIngredients.map((i) => i.productCode)}
                strategy={verticalListSortingStrategy}
              >
                <tbody className="divide-y divide-gray-100">
                  {sortedIngredients.map((ingredient, index) => (
                    <CompositionTabRow
                      key={ingredient.productCode}
                      ingredient={ingredient}
                      displayOrder={isEditMode ? index + 1 : ingredient.order}
                      isEditMode={isEditMode}
                    />
                  ))}
                </tbody>
              </SortableContext>
            </table>
          </div>
        </div>
      </DndContext>
    </div>
  );
};

export default CompositionTab;
