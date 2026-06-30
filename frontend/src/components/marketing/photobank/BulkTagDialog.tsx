import React, { useState, useRef, useEffect } from "react";
import { X } from "lucide-react";
import { useBulkAddPhotoTag } from "../../../api/hooks/usePhotobank";
import type { TagWithCountDto } from "../../../api/hooks/usePhotobank";
import { useToast } from "../../../contexts/ToastContext";
import { useTelemetry } from "../../../telemetry/useTelemetry";

const MAX_AUTOCOMPLETE_SUGGESTIONS = 8;
const BULK_TAG_LIMIT_EXCEEDED_CODE = 2606;

interface BulkTagDialogProps {
  search: string;
  selectedTagNames: string[];
  totalMatching: number;
  existingTags: TagWithCountDto[];
  onClose: () => void;
}

function FilterChip({ label }: { label: string }) {
  return (
    <span className="px-2 py-0.5 bg-secondary-blue-pale text-primary-blue rounded-full text-xs">
      {label}
    </span>
  );
}

export default function BulkTagDialog({
  search,
  selectedTagNames,
  totalMatching,
  existingTags,
  onClose,
}: BulkTagDialogProps) {
  const [tagName, setTagName] = useState("");
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const { showSuccess } = useToast();
  const { trackEvent } = useTelemetry();
  const { mutateAsync, isPending } = useBulkAddPhotoTag();
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const suggestions = tagName.trim()
    ? existingTags
        .filter((t) => t.name.toLowerCase().includes(tagName.toLowerCase()))
        .slice(0, MAX_AUTOCOMPLETE_SUGGESTIONS)
    : [];

  function handleBackdropClick(e: React.MouseEvent<HTMLDivElement>) {
    if (e.target === e.currentTarget) {
      onClose();
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setErrorMessage(null);

    try {
      const result = await mutateAsync({
        tags: selectedTagNames.length > 0 ? selectedTagNames : undefined,
        search: search || undefined,
        tagName: tagName.trim(),
      });

      if (result.success) {
        showSuccess(
          "Štítek přidán",
          `Přidán štítek "${result.tagName}" k ${result.addedCount} fotkám (${result.alreadyTaggedCount} už ho mělo).`,
        );
        trackEvent(
          "PhotobankBulkTagApplied",
          { tagCount: String(selectedTagNames.length > 0 ? selectedTagNames.length : 1) },
          { photoCount: result.addedCount ?? 0 },
        );
        onClose();
        return;
      }

      if (result.errorCode === BULK_TAG_LIMIT_EXCEEDED_CODE) {
        setErrorMessage(
          `Filtr odpovídá příliš mnoha fotkám (${result.params?.Count ?? "?"}). Upřesněte filtry (max ${result.params?.Limit ?? "5 000"}).`,
        );
        return;
      }

      setErrorMessage("Operace selhala. Zkuste to prosím znovu.");
    } catch {
      setErrorMessage("Operace selhala. Zkuste to prosím znovu.");
    }
  }

  const isSubmitDisabled = tagName.trim() === "" || isPending;

  return (
    <div
      data-testid="bulk-tag-dialog-backdrop"
      className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center"
      onClick={handleBackdropClick}
    >
      <div
        className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark w-full max-w-md p-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="bulk-tag-dialog-title"
      >
        {/* Header */}
        <div className="flex items-center justify-between mb-4">
          <h2 id="bulk-tag-dialog-title" className="text-base font-semibold text-gray-900 dark:text-graphite-text">
            Hromadné štítkování
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600"
            aria-label="Zavřít"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          {/* Filter summary */}
          <div className="mb-3">
            <p className="text-xs text-gray-500 dark:text-graphite-muted mb-1.5">Bude aplikováno na:</p>
            <div className="flex flex-wrap gap-1.5">
              {search && <FilterChip label={`Název: "${search}"`} />}
              {selectedTagNames.map((name) => (
                <FilterChip key={name} label={name} />
              ))}
            </div>
          </div>

          {/* Count line */}
          <p className="text-xs text-gray-500 dark:text-graphite-muted mb-4">
            Celkem <strong>{totalMatching}</strong> fotek.
          </p>

          {/* Tag input with autocomplete */}
          <div className="mb-4">
            <label
              htmlFor="bulk-tag-input"
              className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1"
            >
              Štítek
            </label>
            <div className="relative">
              <input
                id="bulk-tag-input"
                ref={inputRef}
                type="text"
                value={tagName}
                onChange={(e) => {
                  setTagName(e.target.value);
                  setShowSuggestions(true);
                  setErrorMessage(null);
                }}
                className="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md focus:outline-none focus:ring-1 focus:ring-primary-blue"
                placeholder="Zadejte název štítku"
                autoComplete="off"
              />
              {showSuggestions && suggestions.length > 0 && tagName.length > 0 && (
                <ul className="absolute z-10 w-full bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark mt-1 max-h-48 overflow-y-auto">
                  {suggestions.map((tag) => (
                    <li
                      key={tag.id}
                      className="px-3 py-1.5 text-sm hover:bg-gray-50 dark:hover:bg-white/5 cursor-pointer"
                      onMouseDown={(e) => {
                        // Prevent blur before click registers
                        e.preventDefault();
                        setTagName(tag.name);
                        setShowSuggestions(false);
                        setErrorMessage(null);
                      }}
                    >
                      {tag.name}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>

          {/* Inline error */}
          {errorMessage && (
            <p className="text-sm text-red-600 dark:text-red-400 mb-3">{errorMessage}</p>
          )}

          {/* Footer buttons */}
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              className="px-3 py-1.5 text-sm border border-gray-300 dark:border-graphite-border rounded-md text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={isSubmitDisabled}
              className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5"
            >
              {isPending && (
                <span className="w-3.5 h-3.5 border-2 border-white border-t-transparent rounded-full animate-spin" />
              )}
              Použít
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
