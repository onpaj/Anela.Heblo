import React, { useRef, useState } from "react";
import { Search } from "lucide-react";
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
import { useIsMobile } from "../../hooks/useMediaQuery";

export type TransferItem = {
  id: string;
  label: string;
  sublabel?: string;
  badge?: string;
};

interface TransferListProps {
  available: TransferItem[];
  assignedIds: string[];
  onChange: (ids: string[]) => void;
  groupBy?: (item: TransferItem) => string;
  labels?: { available?: string; assigned?: string };
  fillHeight?: boolean;
  searchable?: boolean;
  searchPlaceholder?: string;
  highlightedIds?: string[];
  highlightLabel?: string;
}

function matchesQuery(item: TransferItem, query: string): boolean {
  return (
    item.label.toLowerCase().includes(query) ||
    (item.sublabel?.toLowerCase().includes(query) ?? false)
  );
}

interface ItemRowProps {
  item: TransferItem;
  direction: "assign" | "remove";
  onMove: () => void;
  highlighted?: boolean;
  highlightLabel?: string;
}

function buildGroups(
  items: TransferItem[],
  groupByFn: (item: TransferItem) => string,
): Map<string, TransferItem[]> {
  const map = new Map<string, TransferItem[]>();
  for (const item of items) {
    const key = groupByFn(item);
    const bucket = map.get(key);
    map.set(key, bucket ? [...bucket, item] : [item]);
  }
  return map;
}

function ItemRow({
  item,
  direction,
  onMove,
  highlighted,
  highlightLabel,
}: ItemRowProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } =
    useDraggable({ id: item.id });
  const style = transform
    ? { transform: CSS.Transform.toString(transform) }
    : undefined;
  const rowColors = highlighted
    ? "border-emerald-200 bg-emerald-50 hover:bg-emerald-100"
    : "border-gray-200 bg-white hover:bg-gray-50";
  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`flex items-center justify-between px-3 py-2 rounded border ${rowColors} cursor-grab${isDragging ? " opacity-50" : ""}`}
      {...attributes}
      {...listeners}
    >
      <div className="flex flex-col min-w-0 flex-1">
        <div className="flex items-center gap-2 min-w-0">
          <span className="text-sm text-gray-900 truncate">{item.label}</span>
          {highlighted && highlightLabel && (
            <span className="flex-shrink-0 text-xs px-1.5 py-0.5 rounded bg-emerald-100 text-emerald-700 font-medium">
              {highlightLabel}
            </span>
          )}
          {item.badge && (
            <span className="flex-shrink-0 text-xs px-1.5 py-0.5 rounded bg-amber-100 text-amber-700 font-medium">
              {item.badge}
            </span>
          )}
        </div>
        {item.sublabel && (
          <span className="text-xs text-gray-500 truncate">
            {item.sublabel}
          </span>
        )}
      </div>
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          onMove();
        }}
        aria-label={
          direction === "assign"
            ? `Assign ${item.label}`
            : `Remove ${item.label}`
        }
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
}

interface DropZoneProps {
  id: string;
  label: string;
  emptyMessage: string;
  children: React.ReactNode;
  isEmpty: boolean;
  variant: "left" | "right";
  fillHeight?: boolean;
}

function DropZone({
  id,
  label,
  emptyMessage,
  children,
  isEmpty,
  variant,
  fillHeight,
}: DropZoneProps) {
  const { setNodeRef, isOver } = useDroppable({ id });
  return (
    <div
      ref={setNodeRef}
      className={`border rounded-lg p-3 ${fillHeight ? "flex flex-col h-full min-h-0" : "min-h-48"} ${
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
      <div
        className={`space-y-1 ${fillHeight ? "flex-1 min-h-0 overflow-y-auto" : ""}`}
      >
        {children}
        {isEmpty && (
          <div className="text-sm text-gray-400 text-center py-6">
            {emptyMessage}
          </div>
        )}
      </div>
    </div>
  );
}

function TransferList({
  available,
  assignedIds,
  onChange,
  groupBy,
  labels = {},
  fillHeight,
  searchable,
  searchPlaceholder = "Search…",
  highlightedIds,
  highlightLabel,
}: TransferListProps) {
  const isMobile = useIsMobile();
  const highlighted = new Set(highlightedIds);
  // A small activation distance keeps the per-row +/− button clicks from being
  // swallowed by the drag sensor — a plain click no longer starts a drag.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
    useSensor(KeyboardSensor),
  );

  const [query, setQuery] = useState("");
  const [activeTab, setActiveTab] = useState<"available" | "assigned">(
    "available",
  );
  const listRef = useRef<HTMLDivElement>(null);
  const trimmedQuery = query.trim().toLowerCase();

  const availableItems = available
    .filter((item) => !assignedIds.includes(item.id))
    .filter((item) => !trimmedQuery || matchesQuery(item, trimmedQuery));
  const assignedItems = available
    .filter((item) => assignedIds.includes(item.id))
    .filter((item) => !trimmedQuery || matchesQuery(item, trimmedQuery));

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

  const renderRows = (
    rowItems: TransferItem[],
    direction: "assign" | "remove",
    grouped: Map<string, TransferItem[]> | null,
  ) => {
    const row = (item: TransferItem) => (
      <ItemRow
        key={item.id}
        item={item}
        direction={direction}
        onMove={() =>
          direction === "assign"
            ? handleAssign(item.id)
            : handleRemove(item.id)
        }
        highlighted={highlighted.has(item.id)}
        highlightLabel={highlightLabel}
      />
    );
    if (!grouped) return rowItems.map(row);
    return Array.from(grouped.entries()).map(([section, sectionItems]) => (
      <div key={section}>
        <div className="text-xs font-medium text-gray-500 px-1 pt-3 pb-0.5 first:pt-0">
          {section}
        </div>
        {sectionItems.map(row)}
      </div>
    ));
  };

  const searchBar = searchable && (
    <div className="relative mb-3 flex-shrink-0">
      <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
        <Search className="h-4 w-4 text-gray-400" />
      </div>
      <input
        type="text"
        aria-label={searchPlaceholder}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={searchPlaceholder}
        className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 text-sm border border-gray-300 rounded-md"
      />
    </div>
  );

  if (isMobile) {
    const showAvailable = activeTab === "available";
    const paneItems = showAvailable ? availableItems : assignedItems;
    const direction: "assign" | "remove" = showAvailable ? "assign" : "remove";
    const grouped = showAvailable ? availableGroups : null;
    const emptyMessage = showAvailable ? "All items assigned" : "None assigned";

    const switchTab = (tab: "available" | "assigned") => {
      setActiveTab(tab);
      if (listRef.current) listRef.current.scrollTop = 0;
    };

    const tabClass = (active: boolean) =>
      `flex-1 px-3 py-2 text-sm font-medium border-b-2 ${
        active
          ? "border-indigo-600 text-indigo-600"
          : "border-transparent text-gray-500 hover:text-gray-700"
      }`;

    return (
      <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
        <div>
          {searchBar}
          <div className="flex flex-shrink-0 mb-2" role="tablist">
            <button
              type="button"
              role="tab"
              aria-selected={showAvailable}
              onClick={() => switchTab("available")}
              className={tabClass(showAvailable)}
            >
              {labels.available ?? "Available"} ({availableItems.length})
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={!showAvailable}
              onClick={() => switchTab("assigned")}
              className={tabClass(!showAvailable)}
            >
              {labels.assigned ?? "Assigned"} ({assignedItems.length})
            </button>
          </div>
          <div
            ref={listRef}
            className={`space-y-1 overflow-y-auto ${
              fillHeight ? "max-h-[60vh]" : "min-h-48"
            }`}
          >
            {renderRows(paneItems, direction, grouped)}
            {paneItems.length === 0 && (
              <div className="text-sm text-gray-400 text-center py-6">
                {emptyMessage}
              </div>
            )}
          </div>
        </div>
      </DndContext>
    );
  }

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <div className={fillHeight ? "flex flex-col h-full min-h-0" : ""}>
        {searchBar}
        <div
          className={`grid grid-cols-2 gap-4${fillHeight ? " flex-1 min-h-0" : ""}`}
        >
          <DropZone
            id="available"
            label={labels.available ?? "Available"}
            emptyMessage="All items assigned"
            isEmpty={availableItems.length === 0}
            variant="left"
            fillHeight={fillHeight}
          >
            {renderRows(availableItems, "assign", availableGroups)}
          </DropZone>

          <DropZone
            id="assigned"
            label={labels.assigned ?? "Assigned"}
            emptyMessage="None assigned"
            isEmpty={assignedItems.length === 0}
            variant="right"
            fillHeight={fillHeight}
          >
            {renderRows(assignedItems, "remove", null)}
          </DropZone>
        </div>
      </div>
    </DndContext>
  );
}

export default TransferList;
