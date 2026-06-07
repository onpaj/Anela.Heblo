import React from "react";
import {
  DndContext,
  useDroppable,
  useDraggable,
  DragEndEvent,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import { CSS } from "@dnd-kit/utilities";

export type TransferItem = {
  id: string;
  label: string;
  sublabel?: string;
};

interface TransferListProps {
  available: TransferItem[];
  assignedIds: string[];
  onChange: (ids: string[]) => void;
  groupBy?: (item: TransferItem) => string;
  labels?: { available?: string; assigned?: string };
}

interface ItemRowProps {
  item: TransferItem;
  direction: "assign" | "remove";
  onMove: () => void;
}

function buildGroups(
  items: TransferItem[],
  groupByFn: (item: TransferItem) => string
): Map<string, TransferItem[]> {
  const map = new Map<string, TransferItem[]>();
  for (const item of items) {
    const key = groupByFn(item);
    const bucket = map.get(key);
    map.set(key, bucket ? [...bucket, item] : [item]);
  }
  return map;
}

function ItemRow({ item, direction, onMove }: ItemRowProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } =
    useDraggable({ id: item.id });
  const style = transform ? { transform: CSS.Transform.toString(transform) } : undefined;
  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`flex items-center justify-between px-3 py-2 rounded border border-gray-200 bg-white hover:bg-gray-50 cursor-grab${isDragging ? " opacity-50" : ""}`}
      {...attributes}
      {...listeners}
    >
      <div className="flex flex-col min-w-0 flex-1">
        <span className="text-sm text-gray-900 truncate">{item.label}</span>
        {item.sublabel && (
          <span className="text-xs text-gray-500 truncate">{item.sublabel}</span>
        )}
      </div>
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          onMove();
        }}
        aria-label={direction === "assign" ? `Assign ${item.label}` : `Remove ${item.label}`}
        className={`ml-3 flex-shrink-0 w-6 h-6 rounded flex items-center justify-center text-sm font-bold ${
          direction === "assign"
            ? "text-indigo-600 hover:bg-indigo-100"
            : "text-red-500 hover:bg-red-100"
        }`}
      >
        {direction === "assign" ? "+" : "−"}
      </button>
    </div>
  );
};

interface DropZoneProps {
  id: string;
  label: string;
  emptyMessage: string;
  children: React.ReactNode;
  isEmpty: boolean;
  variant: "left" | "right";
}

function DropZone({
  id,
  label,
  emptyMessage,
  children,
  isEmpty,
  variant,
}: DropZoneProps) {
  const { setNodeRef, isOver } = useDroppable({ id });
  return (
    <div
      ref={setNodeRef}
      className={`border rounded-lg p-3 min-h-48 ${
        isOver
          ? "bg-indigo-50 border-indigo-400"
          : variant === "left"
          ? "border-gray-300 bg-gray-50"
          : "border-gray-300 bg-white"
      }`}
    >
      <div className="text-xs font-semibold text-gray-600 uppercase tracking-wider mb-2">
        {label}
      </div>
      <div className="space-y-1">
        {children}
        {isEmpty && (
          <div className="text-sm text-gray-400 text-center py-6">{emptyMessage}</div>
        )}
      </div>
    </div>
  );
};

function TransferList({
  available,
  assignedIds,
  onChange,
  groupBy,
  labels = {},
}: TransferListProps) {
  const sensors = useSensors(useSensor(PointerSensor), useSensor(KeyboardSensor));

  const availableItems = available.filter((item) => !assignedIds.includes(item.id));
  const assignedItems = available.filter((item) => assignedIds.includes(item.id));

  const handleAssign = (id: string) => onChange([...assignedIds, id]);
  const handleRemove = (id: string) =>
    onChange(assignedIds.filter((existing) => existing !== id));

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over) return;
    const itemId = String(active.id);
    const targetZone = String(over.id);
    if (targetZone === "assigned" && !assignedIds.includes(itemId)) {
      handleAssign(itemId);
    } else if (targetZone === "available" && assignedIds.includes(itemId)) {
      handleRemove(itemId);
    }
  };

  const availableGroups = groupBy ? buildGroups(availableItems, groupBy) : null;

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <div className="grid grid-cols-2 gap-4">
        <DropZone
          id="available"
          label={labels.available ?? "Available"}
          emptyMessage="All items assigned"
          isEmpty={availableItems.length === 0}
          variant="left"
        >
          {availableGroups
            ? Array.from(availableGroups.entries()).map(([section, sectionItems]) => (
                <div key={section}>
                  <div className="text-xs font-medium text-gray-500 px-1 pt-3 pb-0.5 first:pt-0">
                    {section}
                  </div>
                  {sectionItems.map((item) => (
                    <ItemRow
                      key={item.id}
                      item={item}
                      direction="assign"
                      onMove={() => handleAssign(item.id)}
                    />
                  ))}
                </div>
              ))
            : availableItems.map((item) => (
                <ItemRow
                  key={item.id}
                  item={item}
                  direction="assign"
                  onMove={() => handleAssign(item.id)}
                />
              ))}
        </DropZone>

        <DropZone
          id="assigned"
          label={labels.assigned ?? "Assigned"}
          emptyMessage="None assigned"
          isEmpty={assignedItems.length === 0}
          variant="right"
        >
          {assignedItems.map((item) => (
            <ItemRow
              key={item.id}
              item={item}
              direction="remove"
              onMove={() => handleRemove(item.id)}
            />
          ))}
        </DropZone>
      </div>
    </DndContext>
  );
};

export default TransferList;
