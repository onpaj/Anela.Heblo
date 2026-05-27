import React, { useEffect, useState } from 'react';
import { Loader2, AlertCircle, Beaker, Pencil, Save, X, Plus } from 'lucide-react';
import {
  DndContext,
  closestCenter,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
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

/** Drop-zone row rendered for an empty phase in edit mode. */
const PhaseDropZoneRow: React.FC<{ phase: string; columnCount: number }> = ({ phase, columnCount }) => {
  const { setNodeRef, isOver } = useDroppable({ id: `phase:${phase}` });
  return (
    <tr>
      <td
        colSpan={columnCount}
        ref={setNodeRef}
        className={`py-4 text-center text-sm border-2 border-dashed rounded transition-colors ${
          isOver ? 'border-indigo-400 bg-indigo-50 text-indigo-600' : 'border-gray-200 text-gray-400'
        }`}
      >
        Přetáhněte ingredience sem pro fázi {phase}
      </td>
    </tr>
  );
};

const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);
  const updateOrder = useUpdateProductCompositionOrder();

  const [isEditMode, setIsEditMode] = useState(false);
  const [draftOrder, setDraftOrder] = useState<IngredientDto[] | null>(null);
  const [draftPhases, setDraftPhases] = useState<string[]>([]);
  const [sortConfig, setSortConfig] = useState<{
    key: keyof IngredientDto;
    direction: 'asc' | 'desc';
  } | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const ingredients = React.useMemo(() => data?.ingredients ?? [], [data?.ingredients]);

  useEffect(() => {
    if (!isEditMode) {
      setDraftOrder(null);
      setDraftPhases([]);
    }
  }, [ingredients, isEditMode]);

  const sortedIngredients = React.useMemo(() => {
    if (isEditMode) {
      return draftOrder ?? ingredients;
    }
    if (!sortConfig) return ingredients;

    return [...ingredients].sort((a, b) => {
      const aValue = a[sortConfig.key];
      const bValue = b[sortConfig.key];
      if (typeof aValue === 'number' && typeof bValue === 'number') {
        return sortConfig.direction === 'asc' ? aValue - bValue : bValue - aValue;
      }
      return sortConfig.direction === 'asc'
        ? String(aValue).localeCompare(String(bValue), 'cs')
        : String(bValue).localeCompare(String(aValue), 'cs');
    });
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

    const overId = String(over.id);

    setDraftOrder((current) => {
      const list = current ?? ingredients;

      if (overId.startsWith('phase:')) {
        // Dropped onto an empty phase drop zone — assign phase and move to end of that phase's block.
        const targetPhase = overId.slice('phase:'.length);
        const oldIndex = list.findIndex((i) => i.productCode === active.id);
        if (oldIndex < 0) return current;

        const activeItem = { ...list[oldIndex], phaseLabel: targetPhase };
        const withoutActive = list.filter((_, idx) => idx !== oldIndex);

        // Insert after the last ingredient already in the target phase (or append to end).
        const lastPhaseIdx = withoutActive.reduce<number>(
          (acc, ing, idx) => (ing.phaseLabel === targetPhase ? idx : acc),
          -1,
        );
        const insertAt = lastPhaseIdx >= 0 ? lastPhaseIdx + 1 : withoutActive.length;
        const result = [...withoutActive];
        result.splice(insertAt, 0, activeItem);
        return result;
      }

      // Dropped onto another ingredient — reorder and inherit that ingredient's phaseLabel.
      const oldIndex = list.findIndex((i) => i.productCode === active.id);
      const newIndex = list.findIndex((i) => i.productCode === over.id);
      if (oldIndex < 0 || newIndex < 0) return current;

      const targetPhase = list[newIndex].phaseLabel ?? null;
      const moved = arrayMove(list, oldIndex, newIndex);
      return moved.map((ing, idx) =>
        idx === newIndex ? { ...ing, phaseLabel: targetPhase } : ing,
      );
    });
  };

  const enterEditMode = () => {
    const initialDraft = ingredients.map((ing) => ({ ...ing }));
    setDraftOrder(initialDraft);
    // Seed draftPhases from existing phase labels (sorted A→Z).
    const existingPhases = Array.from(
      new Set(
        ingredients
          .map((i) => i.phaseLabel)
          .filter((l): l is string => typeof l === 'string' && l.length === 1),
      ),
    ).sort();
    setDraftPhases(existingPhases);
    setSortConfig(null);
    setSaveError(null);
    setIsEditMode(true);
  };

  const cancelEdit = () => {
    setDraftOrder(null);
    setDraftPhases([]);
    setSaveError(null);
    setIsEditMode(false);
  };

  const addPhase = () => {
    const next = LETTERS.split('').find((l) => !draftPhases.includes(l));
    if (!next) return; // All 26 letters used.
    setDraftPhases((prev) => [...prev, next].sort());
  };

  const removePhase = (phase: string) => {
    setDraftOrder((current) => {
      const list = current ?? ingredients;
      return list.map((ing) =>
        ing.phaseLabel === phase ? { ...ing, phaseLabel: null } : ing,
      );
    });
    setDraftPhases((prev) => prev.filter((p) => p !== phase));
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
          phaseLabel: ing.phaseLabel ?? null,
        })),
      });
      setIsEditMode(false);
      setDraftOrder(null);
      setDraftPhases([]);
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
          <div>Chyba při načítání složení: {(error as Error).message}</div>
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

  // Compute empty phases (those in draftPhases not yet referenced by any ingredient).
  const emptyPhases = isEditMode
    ? draftPhases.filter((p) => !sortedIngredients.some((i) => i.phaseLabel === p))
    : [];

  const columnCount = isEditMode ? 6 : 4;

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <Beaker className="h-5 w-5 mr-2 text-gray-500" />
          Složení ({sortedIngredients.length} ingrediencí)
        </h3>
        <div className="flex items-center space-x-2">
          {isEditMode && (
            <button
              type="button"
              onClick={addPhase}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-indigo-300 rounded-md text-indigo-700 bg-indigo-50 hover:bg-indigo-100"
            >
              <Plus className="h-4 w-4 mr-1" />
              Přidat fázi
            </button>
          )}
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
                  {isEditMode && <th className="text-center py-3 px-3 font-medium text-gray-700 w-16">Fáze</th>}
                </tr>
              </thead>
              <SortableContext
                items={sortedIngredients.map((i) => i.productCode)}
                strategy={verticalListSortingStrategy}
              >
                <tbody className="divide-y divide-gray-100">
                  {sortedIngredients.map((ingredient, index) => {
                    const currentPhase = ingredient.phaseLabel ?? null;
                    const prevPhase =
                      index > 0 ? (sortedIngredients[index - 1].phaseLabel ?? null) : null;
                    const nextPhase =
                      index < sortedIngredients.length - 1
                        ? (sortedIngredients[index + 1].phaseLabel ?? null)
                        : null;
                    const isFirstInPhase = !!currentPhase && currentPhase !== prevPhase;
                    const isLastInPhase = !!currentPhase && currentPhase !== nextPhase;

                    return (
                      <React.Fragment key={ingredient.productCode}>
                        {/* Phase header strip — shown in both read and edit mode */}
                        {isFirstInPhase && currentPhase && (
                          <tr className="bg-indigo-50 border-t border-indigo-200">
                            <td colSpan={columnCount} className="px-4 py-1">
                              <div className="flex items-center justify-between">
                                <span className="text-sm font-semibold text-indigo-700">
                                  Fáze {currentPhase}
                                </span>
                                {isEditMode && (
                                  <button
                                    type="button"
                                    onClick={() => removePhase(currentPhase!)}
                                    className="text-indigo-400 hover:text-red-500 text-lg leading-none px-1"
                                    title={`Odebrat fázi ${currentPhase}`}
                                  >
                                    ×
                                  </button>
                                )}
                              </div>
                            </td>
                          </tr>
                        )}
                        <CompositionTabRow
                          ingredient={ingredient}
                          displayOrder={isEditMode ? index + 1 : ingredient.order}
                          isEditMode={isEditMode}
                          isFirstInPhase={isFirstInPhase}
                          isLastInPhase={isLastInPhase}
                        />
                      </React.Fragment>
                    );
                  })}

                  {/* Empty phase drop zones — for phases that have no ingredients yet */}
                  {emptyPhases.map((phase) => (
                    <React.Fragment key={`empty-phase-${phase}`}>
                      <tr className="bg-indigo-50 border-t border-indigo-200">
                        <td colSpan={columnCount} className="px-4 py-1">
                          <div className="flex items-center justify-between">
                            <span className="text-sm font-semibold text-indigo-700">
                              Fáze {phase}
                            </span>
                            <button
                              type="button"
                              onClick={() => removePhase(phase)}
                              className="text-indigo-400 hover:text-red-500 text-lg leading-none px-1"
                              title={`Odebrat fázi ${phase}`}
                            >
                              ×
                            </button>
                          </div>
                        </td>
                      </tr>
                      <PhaseDropZoneRow phase={phase} columnCount={columnCount} />
                    </React.Fragment>
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
