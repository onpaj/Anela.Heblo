import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { GripVertical } from 'lucide-react';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';

interface CompositionTabRowProps {
  ingredient: IngredientDto;
  displayOrder: number;
  isEditMode: boolean;
  /** True when this row is the first ingredient of its phase group. */
  isFirstInPhase: boolean;
  /** True when this row is the last ingredient of its phase group. */
  isLastInPhase: boolean;
}

export const CompositionTabRow: React.FC<CompositionTabRowProps> = ({
  ingredient,
  displayOrder,
  isEditMode,
  isFirstInPhase,
  isLastInPhase,
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: ingredient.productCode });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.6 : 1,
  };

  const phaseLabel = ingredient.phaseLabel ?? null;

  // Build Fáze cell class names using left/top/bottom borders as a bracket indicator.
  const fazeCellClasses = [
    'py-3 px-3 text-center w-16',
    phaseLabel ? 'border-l-2 border-indigo-400' : '',
    phaseLabel && isFirstInPhase ? 'border-t-2 border-indigo-400' : '',
    phaseLabel && isLastInPhase ? 'border-b-2 border-indigo-400' : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <tr
      ref={setNodeRef}
      style={style}
      className={`hover:bg-gray-50 ${isDragging ? 'bg-indigo-50' : ''}`}
    >
      {isEditMode && (
        <td
          className="py-3 px-2 w-8 text-gray-400 cursor-grab active:cursor-grabbing"
          {...attributes}
          {...listeners}
        >
          <GripVertical className="h-4 w-4" />
        </td>
      )}
      <td className="py-3 px-4 text-right text-gray-700 w-16">{displayOrder}</td>
      <td className="py-3 px-4 text-gray-900">{ingredient.productName}</td>
      <td className="py-3 px-4 text-gray-900 font-medium">{ingredient.productCode}</td>
      <td className="py-3 px-4 text-right text-gray-900 font-medium">
        {ingredient.amount.toLocaleString('cs-CZ', {
          minimumFractionDigits: 2,
          maximumFractionDigits: 4,
        })}
      </td>
      {isEditMode && (
        <td className={fazeCellClasses}>
          {isFirstInPhase && phaseLabel ? (
            <span className="text-indigo-600 font-bold text-sm">{phaseLabel}</span>
          ) : null}
        </td>
      )}
    </tr>
  );
};
