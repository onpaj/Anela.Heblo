import React, { useState } from "react";
import type { TagWithCountDto } from "../../../api/hooks/usePhotobank";

const MAX_SELECTION = 5000;

interface PhotobankBulkActionBarProps {
  selectedCount: number;
  existingTags: TagWithCountDto[];
  isApplying: boolean;
  isAutoTagging?: boolean;
  onApplyTag: (tagName: string) => Promise<void>;
  onAutoTag?: () => void;
  onClear: () => void;
}

export default function PhotobankBulkActionBar({
  selectedCount,
  existingTags,
  isApplying,
  isAutoTagging = false,
  onApplyTag,
  onAutoTag,
  onClear,
}: PhotobankBulkActionBarProps) {
  const [tagInput, setTagInput] = useState("");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const isOverLimit = selectedCount > MAX_SELECTION;
  const isApplyDisabled = tagInput.trim() === "" || isApplying || isOverLimit;

  async function handleApply() {
    const trimmed = tagInput.trim();
    if (!trimmed || isApplying || isOverLimit) return;

    setErrorMessage(null);
    try {
      await onApplyTag(trimmed);
      setTagInput("");
    } catch {
      setErrorMessage("Operace selhala. Zkuste to prosím znovu.");
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter") {
      void handleApply();
    }
  }

  return (
    <div
      data-testid="bulk-action-bar"
      className="flex items-center gap-3 px-4 py-2 bg-secondary-blue-pale border-b border-primary-blue/20"
    >
      {/* Left: selection count */}
      <span className="text-sm font-medium text-primary-blue whitespace-nowrap">
        {selectedCount} fotek vybráno
      </span>

      {/* Middle: tag input */}
      <div className="flex-1 flex flex-col gap-0.5">
        <input
          data-testid="bulk-tag-input"
          type="text"
          list="bulk-tag-datalist"
          value={tagInput}
          onChange={(e) => {
            setTagInput(e.target.value);
            setErrorMessage(null);
          }}
          onKeyDown={handleKeyDown}
          placeholder="Název štítku"
          className="px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-blue w-full max-w-xs"
          autoComplete="off"
        />
        <datalist id="bulk-tag-datalist">
          {existingTags.map((tag) => (
            <option key={tag.id} value={tag.name} />
          ))}
        </datalist>
        {errorMessage && (
          <p className="text-xs text-red-600">{errorMessage}</p>
        )}
        {isOverLimit && (
          <p className="text-xs text-amber-600">
            Vyberte max. 5 000 fotek
          </p>
        )}
      </div>

      {/* Right: action buttons */}
      <div className="flex items-center gap-2 flex-shrink-0">
        <button
          data-testid="bulk-apply-btn"
          type="button"
          disabled={isApplyDisabled}
          onClick={() => void handleApply()}
          className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5"
        >
          {isApplying && (
            <span className="w-3.5 h-3.5 border-2 border-white border-t-transparent rounded-full animate-spin" />
          )}
          Přidat štítek
        </button>
        {onAutoTag && (
          <button
            data-testid="bulk-auto-tag-btn"
            type="button"
            disabled={isAutoTagging || isOverLimit}
            onClick={onAutoTag}
            className="px-3 py-1.5 text-sm border border-primary-blue text-primary-blue rounded-md hover:bg-secondary-blue-pale disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5"
          >
            {isAutoTagging && (
              <span className="w-3.5 h-3.5 border-2 border-primary-blue border-t-transparent rounded-full animate-spin" />
            )}
            {isAutoTagging ? "Tagování…" : "Auto-tag výběru"}
          </button>
        )}
        <button
          data-testid="bulk-clear-btn"
          type="button"
          onClick={onClear}
          className="px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50"
        >
          Zrušit výběr
        </button>
      </div>
    </div>
  );
}
