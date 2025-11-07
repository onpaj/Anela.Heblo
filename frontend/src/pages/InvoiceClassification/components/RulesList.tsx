import React from 'react';
import { Edit, Trash2, GripVertical } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  DragEndEvent,
} from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import {
  useSortable,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ClassificationRule, useClassificationRuleTypes } from '../../../api/hooks/useInvoiceClassification';
import { useDepartments } from '../../../api/hooks/useDepartments';

interface RulesListProps {
  rules: ClassificationRule[];
  onEdit: (rule: ClassificationRule) => void;
  onDelete: (ruleId: string) => void;
  onReorder: (ruleIds: string[]) => void;
  isReordering: boolean;
  isDeleting: boolean;
}

interface SortableRuleItemProps {
  rule: ClassificationRule;
  onEdit: (rule: ClassificationRule) => void;
  onDelete: (ruleId: string) => void;
  isDeleting: boolean;
}

const SortableRuleItem: React.FC<SortableRuleItemProps> = ({ rule, onEdit, onDelete, isDeleting }) => {
  const { t } = useTranslation();
  const { data: ruleTypes = [] } = useClassificationRuleTypes();
  const { data: departments = [] } = useDepartments();
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: rule.id || `rule-${Math.random()}` });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  const getRuleTypeLabel = (ruleTypeIdentifier: string): string => {
    const ruleType = ruleTypes.find(rt => rt.identifier === ruleTypeIdentifier);
    return ruleType ? (ruleType.displayName || ruleTypeIdentifier) : ruleTypeIdentifier;
  };

  const getDepartmentName = (departmentId: string | null | undefined): string | null => {
    if (!departmentId) return null;
    const department = departments.find(d => d.id === departmentId);
    return department?.name || departmentId;
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className="bg-white border border-gray-200 rounded-lg p-4 shadow-sm hover:shadow-md transition-shadow"
    >
      <div className="flex items-center gap-4">
        <div
          {...attributes}
          {...listeners}
          className="cursor-grab hover:cursor-grabbing text-gray-400 hover:text-gray-600"
        >
          <GripVertical className="w-5 h-5" />
        </div>

        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-4 mb-2">
            <h3 className="text-lg font-medium text-gray-900 truncate">
              {rule.name}
            </h3>
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
              rule.isActive 
                ? 'bg-green-100 text-green-800' 
                : 'bg-gray-100 text-gray-800'
            }`}>
              {rule.isActive 
                ? 'Aktivní' 
                : 'Neaktivní'
              }
            </span>
          </div>
          
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 text-sm text-gray-600">
            <div>
              <span className="font-medium">
                Typ:
              </span>{' '}
              {getRuleTypeLabel(rule.ruleTypeIdentifier || '')}
            </div>
            <div>
              <span className="font-medium">
                Vzor:
              </span>{' '}
              <code className="bg-gray-100 px-1 py-0.5 rounded text-xs">
                {rule.pattern}
              </code>
            </div>
            <div>
              <span className="font-medium">
                Předpis:
              </span>{' '}
              <code className="bg-gray-100 px-1 py-0.5 rounded text-xs">
                {rule.accountingTemplateCode}
              </code>
            </div>
            <div>
              <span className="font-medium">
                Oddělení:
              </span>{' '}
              {getDepartmentName(rule.department) || (
                <span className="italic text-gray-400">Nenastaveno</span>
              )}
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={() => onEdit(rule)}
            className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
            title="Upravit"
          >
            <Edit className="w-4 h-4" />
          </button>
          <button
            onClick={() => rule.id && onDelete(rule.id)}
            disabled={isDeleting}
            className="p-2 text-gray-400 hover:text-red-600 rounded-lg hover:bg-gray-50 disabled:opacity-50"
            title="Smazat"
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
};

const RulesList: React.FC<RulesListProps> = ({ 
  rules, 
  onEdit, 
  onDelete, 
  onReorder, 
  isReordering, 
  isDeleting 
}) => {
  const { t } = useTranslation();
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;

    if (over && active.id !== over.id) {
      const oldIndex = rules.findIndex((rule) => rule.id === active.id);
      const newIndex = rules.findIndex((rule) => rule.id === over.id);

      const newRules = arrayMove(rules, oldIndex, newIndex);
      const reorderedIds = newRules.map(rule => rule.id).filter(id => id !== undefined) as string[];
      onReorder(reorderedIds);
    }
  };

  if (rules.length === 0) {
    return (
      <div className="text-center py-12">
        <p className="text-gray-500 text-lg">
          Nebyla nalezena žádná pravidla klasifikace. Vytvořte své první pravidlo pro začátek.
        </p>
      </div>
    );
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-xl font-semibold text-gray-900">
          Pravidla klasifikace
        </h2>
        <span className="text-sm text-gray-500">
          {rules.length} pravidel
        </span>
      </div>

      <div className="space-y-3">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={handleDragEnd}
        >
          <SortableContext items={rules.map(rule => rule.id || `rule-${Math.random()}`)} strategy={verticalListSortingStrategy}>
            {rules.map((rule) => (
              <SortableRuleItem
                key={rule.id}
                rule={rule}
                onEdit={onEdit}
                onDelete={onDelete}
                isDeleting={isDeleting}
              />
            ))}
          </SortableContext>
        </DndContext>
      </div>

      {isReordering && (
        <div className="mt-4 text-center text-sm text-gray-500">
          {t('invoiceClassification.reordering', 'Updating rule order...')}
        </div>
      )}
    </div>
  );
};

export default RulesList;