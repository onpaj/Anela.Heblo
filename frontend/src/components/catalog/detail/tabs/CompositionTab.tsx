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
          isOver ? 'border-indigo-400 bg-indigo-50 text-indigo-600 dark:border-graphite-accent dark:bg-graphite-accent/10 dark:text-graphite-accent' : 'border-gray-200 text-gray-400 dark:border-graphite-border dark:text-graphite-faint'
        }`}
      >
        Přetáhněte ingredience sem pro fázi {phase}
      </td>
    </tr>
  );
};

const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

/**
 * Stable-sorts items so that phases appear in `phases` order (A→Z), then unphased last.
 * Relative order within each group is preserved (JS sort is stable in ES2019+).
 */
function groupByPhase(items: IngredientDto[], phases: string[]): IngredientDto[] {
  const phaseOrder = new Map<string, number>(phases.map((p, i) => [p, i]));
  return [...items].sort((a, b) => {
    const aOrder = a.phaseLabel != null ? (phaseOrder.get(a.phaseLabel) ?? phases.length) : phases.length;
    const bOrder = b.phaseLabel != null ? (phaseOrder.get(b.phaseLabel) ?? phases.length) : phases.length;
    return aOrder - bOrder;
  });
}

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

  /** Display order number (1-based) for each ingredient in edit mode. */
  const editDisplayOrder = React.useMemo(
    () =>
      isEditMode
        ? new Map(sortedIngredients.map((ing, idx) => [ing.productCode, idx + 1]))
        : null,
    [isEditMode, sortedIngredients],
  );

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
        // Dropped onto an empty phase drop zone — assign phase, then re-sort.
        const targetPhase = overId.slice('phase:'.length);
        const oldIndex = list.findIndex((i) => i.productCode === active.id);
        if (oldIndex < 0) return current;
        const activeItem = { ...list[oldIndex], phaseLabel: targetPhase };
        const withoutActive = list.filter((_, idx) => idx !== oldIndex);
        return groupByPhase([...withoutActive, activeItem], draftPhases);
      }

      // Dropped onto another ingredient — reorder, inherit that ingredient's phaseLabel, re-sort.
      const oldIndex = list.findIndex((i) => i.productCode === active.id);
      const newIndex = list.findIndex((i) => i.productCode === over.id);
      if (oldIndex < 0 || newIndex < 0) return current;

      const targetPhase = list[newIndex].phaseLabel ?? null;
      const moved = arrayMove(list, oldIndex, newIndex);
      const withPhase = moved.map((ing, idx) =>
        idx === newIndex ? { ...ing, phaseLabel: targetPhase } : ing,
      );
      return groupByPhase(withPhase, draftPhases);
    });
  };

  const enterEditMode = () => {
    const existingPhases = Array.from(
      new Set(
        ingredients
          .map((i) => i.phaseLabel)
          .filter((l): l is string => typeof l === 'string' && l.length === 1),
      ),
    ).sort();
    setDraftPhases(existingPhases);
    setDraftOrder(groupByPhase(ingredients.map((ing) => ({ ...ing })), existingPhases));
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
    const newPhases = [...draftPhases, next].sort();
    setDraftOrder((current) => {
      const list = current ?? ingredients;
      const updated = list.map((ing) =>
        ing.phaseLabel == null ? { ...ing, phaseLabel: next } : ing,
      );
      return groupByPhase(updated, newPhases);
    });
    setDraftPhases(newPhases);
  };

  const removePhase = (phase: string) => {
    const newPhases = draftPhases.filter((p) => p !== phase);
    setDraftOrder((current) => {
      const list = current ?? ingredients;
      const updated = list.map((ing) =>
        ing.phaseLabel === phase ? { ...ing, phaseLabel: null } : ing,
      );
      return groupByPhase(updated, newPhases);
    });
    setDraftPhases(newPhases);
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
          <div className="text-gray-500 dark:text-graphite-muted">Načítání složení...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání složení: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  if (ingredients.length === 0) {
    return (
      <div className="text-center py-12 bg-gray-50 dark:bg-graphite-surface-2 rounded-lg">
        <Beaker className="h-12 w-12 mx-auto mb-3 text-gray-300 dark:text-graphite-faint" />
        <p className="text-gray-500 dark:text-graphite-muted mb-2">Tento produkt nemá definované složení</p>
        <p className="text-sm text-gray-400 dark:text-graphite-faint">Výrobní šablona pro tento produkt neexistuje</p>
      </div>
    );
  }

  const columnCount = isEditMode ? 6 : 4;

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text flex items-center">
          <Beaker className="h-5 w-5 mr-2 text-gray-500 dark:text-graphite-muted" />
          Složení ({sortedIngredients.length} ingrediencí)
        </h3>
        <div className="flex items-center space-x-2">
          {isEditMode && (
            <button
              type="button"
              onClick={addPhase}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-indigo-300 rounded-md text-indigo-700 bg-indigo-50 hover:bg-indigo-100 dark:border-graphite-accent dark:text-graphite-accent dark:bg-graphite-accent/10 dark:hover:bg-white/5"
            >
              <Plus className="h-4 w-4 mr-1" />
              Přidat fázi
            </button>
          )}
          {!isEditMode && (
            <button
              type="button"
              onClick={enterEditMode}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50 dark:border-graphite-border dark:text-graphite-muted dark:bg-graphite-surface dark:hover:bg-white/5"
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
                className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50 dark:border-graphite-border dark:text-graphite-muted dark:bg-graphite-surface dark:hover:bg-white/5"
              >
                <X className="h-4 w-4 mr-1.5" />
                Zrušit
              </button>
            </>
          )}
        </div>
      </div>

      {saveError && (
        <div className="flex items-center space-x-2 px-3 py-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded mb-4 dark:text-red-300 dark:bg-red-900/30 dark:border-graphite-border">
          <AlertCircle className="h-4 w-4" />
          <span>{saveError}</span>
        </div>
      )}

      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={handleDragEnd}
      >
        <div className="flex-1 min-h-0 bg-white rounded-lg border border-gray-200 overflow-hidden dark:bg-graphite-surface dark:border-graphite-border">
          <div className="h-full overflow-y-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200 dark:bg-graphite-surface-2 dark:border-graphite-border">
                <tr>
                  {isEditMode && <th className="w-8 py-3 px-2" />}
                  <th className="text-right py-3 px-4 font-medium text-gray-700 dark:text-graphite-muted w-16">#</th>
                  <th
                    className={`text-left py-3 px-4 font-medium text-gray-700 dark:text-graphite-muted ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5'}`}
                    onClick={() => handleSort('productName')}
                  >
                    Název{getSortIcon('productName')}
                  </th>
                  <th
                    className={`text-left py-3 px-4 font-medium text-gray-700 dark:text-graphite-muted ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5'}`}
                    onClick={() => handleSort('productCode')}
                  >
                    Kód{getSortIcon('productCode')}
                  </th>
                  <th
                    className={`text-right py-3 px-4 font-medium text-gray-700 dark:text-graphite-muted ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5'}`}
                    onClick={() => handleSort('amount')}
                  >
                    Množství{getSortIcon('amount')}
                  </th>
                  {isEditMode && <th className="text-center py-3 px-3 font-medium text-gray-700 dark:text-graphite-muted w-16">Fáze</th>}
                </tr>
              </thead>
              <SortableContext
                items={sortedIngredients.map((i) => i.productCode)}
                strategy={verticalListSortingStrategy}
              >
                <tbody className="divide-y divide-gray-100 dark:divide-graphite-border">
                  {isEditMode ? (
                    <>
                      {/* In edit mode iterate draftPhases so phases always appear A→Z. */}
                      {draftPhases.map((phase) => {
                        const phaseIngredients = sortedIngredients.filter(
                          (i) => i.phaseLabel === phase,
                        );
                        return (
                          <React.Fragment key={`phase-section-${phase}`}>
                            <tr className="bg-indigo-50 border-t border-indigo-200 dark:bg-graphite-accent/10 dark:border-graphite-accent">
                              <td colSpan={columnCount} className="px-4 py-1">
                                <div className="flex items-center justify-between">
                                  <span className="text-sm font-semibold text-indigo-700 dark:text-graphite-accent">
                                    Fáze {phase}
                                  </span>
                                  <button
                                    type="button"
                                    onClick={() => removePhase(phase)}
                                    className="text-indigo-400 hover:text-red-500 text-lg leading-none px-1 dark:text-graphite-accent dark:hover:text-red-400"
                                    title={`Odebrat fázi ${phase}`}
                                  >
                                    ×
                                  </button>
                                </div>
                              </td>
                            </tr>
                            {phaseIngredients.length > 0 ? (
                              phaseIngredients.map((ingredient, idxInPhase) => (
                                <CompositionTabRow
                                  key={ingredient.productCode}
                                  ingredient={ingredient}
                                  displayOrder={editDisplayOrder!.get(ingredient.productCode)!}
                                  isEditMode
                                  isFirstInPhase={idxInPhase === 0}
                                  isLastInPhase={idxInPhase === phaseIngredients.length - 1}
                                />
                              ))
                            ) : (
                              <PhaseDropZoneRow phase={phase} columnCount={columnCount} />
                            )}
                          </React.Fragment>
                        );
                      })}
                      {/* Unphased ingredients rendered after all phase groups. */}
                      {sortedIngredients
                        .filter((i) => i.phaseLabel === null)
                        .map((ingredient) => (
                          <CompositionTabRow
                            key={ingredient.productCode}
                            ingredient={ingredient}
                            displayOrder={editDisplayOrder!.get(ingredient.productCode)!}
                            isEditMode
                            isFirstInPhase={false}
                            isLastInPhase={false}
                          />
                        ))}
                    </>
                  ) : (
                    sortedIngredients.map((ingredient, index) => {
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
                          {isFirstInPhase && currentPhase && (
                            <tr className="bg-indigo-50 border-t border-indigo-200 dark:bg-graphite-accent/10 dark:border-graphite-accent">
                              <td colSpan={columnCount} className="px-4 py-1">
                                <span className="text-sm font-semibold text-indigo-700 dark:text-graphite-accent">
                                  Fáze {currentPhase}
                                </span>
                              </td>
                            </tr>
                          )}
                          <CompositionTabRow
                            ingredient={ingredient}
                            displayOrder={ingredient.order}
                            isEditMode={false}
                            isFirstInPhase={isFirstInPhase}
                            isLastInPhase={isLastInPhase}
                          />
                        </React.Fragment>
                      );
                    })
                  )}
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
