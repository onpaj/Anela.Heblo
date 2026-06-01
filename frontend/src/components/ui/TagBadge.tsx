import React from "react";
import { getTagColor } from "./tagColor";

export interface TagBadgeProps {
  name: string;
  variant?: "default" | "overlay";
  onRemove?: () => void;
}

export const TagBadge: React.FC<TagBadgeProps> = ({
  name,
  variant = "default",
  onRemove,
}) => {
  const isOverlay = variant === "overlay";
  const { bg, text } = getTagColor(name, isOverlay);

  return (
    <div data-testid="tag-badge" className={`inline-flex items-center rounded-full text-xs px-2 py-0.5 gap-1 ${bg} ${text}`}>
      {name}
      {onRemove && (
        <button
          type="button"
          onClick={onRemove}
          aria-label={`Odebrat štítek ${name}`}
          className="ml-0.5 hover:opacity-70 rounded-full"
        >
          <span aria-hidden="true">×</span>
        </button>
      )}
    </div>
  );
};

export default TagBadge;
