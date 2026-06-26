import React from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";

interface CollapsibleRailProps {
  side: "left" | "right";
  isOpen: boolean;
  label: string;
  onToggle: () => void;
}

// A 32px-wide vertical strip holding a single chevron toggle for an adjacent
// side panel. The chevron points toward the action: inward to collapse an open
// panel, outward to expand a collapsed one.
const CollapsibleRail: React.FC<CollapsibleRailProps> = ({
  side,
  isOpen,
  label,
  onToggle,
}) => {
  const pointsLeft = side === "left" ? isOpen : !isOpen;
  const Chevron = pointsLeft ? ChevronLeft : ChevronRight;
  const actionLabel = `${isOpen ? "Sbalit" : "Rozbalit"} ${label}`;

  return (
    <div className="flex w-8 flex-shrink-0 items-start justify-center border-x border-gray-200 dark:border-graphite-border bg-gray-50 dark:bg-graphite-surface-2 pt-3">
      <button
        type="button"
        onClick={onToggle}
        data-testid={`collapsible-rail-${side}`}
        aria-label={actionLabel}
        title={actionLabel}
        className="inline-flex h-7 w-7 items-center justify-center rounded-md text-gray-500 dark:text-graphite-muted hover:bg-gray-200 dark:hover:bg-graphite-hover hover:text-gray-700"
      >
        <Chevron className="h-4 w-4" />
      </button>
    </div>
  );
};

export default CollapsibleRail;
