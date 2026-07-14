import React from "react";
import { AlertTriangle, X } from "lucide-react";

interface MeetingReviewLeaveDialogProps {
  isOpen: boolean;
  isSaving: boolean;
  /** Mark the meeting as reviewed ("Schváleno") and then leave. */
  onSave: () => void;
  /** Leave without changing the status — it stays "Ke kontrole". */
  onDiscard: () => void;
  /** Stay on the detail page. */
  onKeepEditing: () => void;
}

/**
 * Shown when the user tries to leave a meeting detail that is still "Ke kontrole".
 * Offers to mark it reviewed, leave it as-is, or stay. Mirrors the shape of the
 * shared UnsavedChangesDialog so it can be driven by `useUnsavedChangesDialog`.
 */
const MeetingReviewLeaveDialog: React.FC<MeetingReviewLeaveDialogProps> = ({
  isOpen,
  isSaving,
  onSave,
  onDiscard,
  onKeepEditing,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={isSaving ? undefined : onKeepEditing}
      />

      {/* Dialog */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
          {/* Close button */}
          <button
            onClick={onKeepEditing}
            className="absolute top-4 right-4 text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5" />
          </button>

          {/* Icon */}
          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-yellow-100 dark:bg-amber-900/30 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-yellow-600 dark:text-amber-400" />
          </div>

          {/* Title */}
          <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text text-center mb-2">
            Porada je stále ke kontrole
          </h3>

          {/* Message */}
          <div className="text-sm text-gray-600 dark:text-graphite-muted text-center mb-6">
            <p>Chcete ji před odchodem označit jako schválenou?</p>
          </div>

          {/* Actions */}
          <div className="flex flex-col gap-3">
            <button
              onClick={onSave}
              disabled={isSaving}
              className="w-full px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 transition-colors disabled:opacity-50"
            >
              {isSaving ? "Ukládání…" : "Označit jako schváleno a odejít"}
            </button>
            <button
              onClick={onDiscard}
              disabled={isSaving}
              className="w-full px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50"
            >
              Nechat ke kontrole a odejít
            </button>
            <button
              onClick={onKeepEditing}
              disabled={isSaving}
              className="w-full px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50"
            >
              Zůstat
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default MeetingReviewLeaveDialog;
