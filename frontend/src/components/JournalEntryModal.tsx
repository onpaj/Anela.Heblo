import { useEffect, useState } from "react";
import { X, AlertCircle } from "lucide-react";
import JournalEntryForm from "./JournalEntryForm";
import { useDeleteJournalEntry } from "../api/hooks/useJournal";
import type { JournalEntryDto } from "../api/generated/api-client";

interface JournalEntryModalProps {
  isOpen: boolean;
  onClose: () => void;
  entry?: JournalEntryDto;
  isEdit?: boolean;
}

export default function JournalEntryModal({
  isOpen,
  onClose,
  entry,
  isEdit = false,
}: JournalEntryModalProps) {
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const deleteEntry = useDeleteJournalEntry();

  // Handle Escape key
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen && !showDeleteConfirm) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
      // Prevent body scroll when modal is open
      document.body.style.overflow = "hidden";
    }

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "unset";
    };
  }, [isOpen, onClose, showDeleteConfirm]);

  if (!isOpen) return null;

  const handleSave = () => {
    onClose();
  };

  const handleCancel = () => {
    onClose();
  };

  const handleDelete = async () => {
    if (entry?.id) {
      try {
        await deleteEntry.mutateAsync(entry.id);
        onClose();
      } catch (error) {
        console.error("Failed to delete journal entry:", error);
      }
    }
  };

  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
      <div className="relative top-4 mx-auto p-0 border w-full max-w-4xl shadow-lg dark:shadow-soft-dark rounded-md bg-white dark:bg-graphite-surface min-h-[calc(100vh-2rem)]">
        {/* Modal Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200 dark:border-graphite-border">
          <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text">
            {isEdit ? "Upravit záznam" : "Nový záznam"}
          </h3>
          <button
            onClick={onClose}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted transition-colors p-1 rounded hover:bg-gray-100 dark:hover:bg-white/10"
            title="Zavřít (Esc)"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Modal Body */}
        <div className="p-6">
          <JournalEntryForm
            entry={entry}
            onSave={handleSave}
            onCancel={handleCancel}
            onDelete={isEdit ? () => setShowDeleteConfirm(true) : undefined}
            isEdit={isEdit}
          />
        </div>
      </div>

      {/* Delete Confirmation Dialog */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-75 overflow-y-auto h-full w-full z-60">
          <div className="relative top-20 mx-auto p-5 border w-96 shadow-lg dark:shadow-soft-dark rounded-md bg-white dark:bg-graphite-surface">
            <div className="mt-3">
              <div className="flex items-center">
                <div className="mx-auto flex-shrink-0 flex items-center justify-center h-12 w-12 rounded-full bg-red-100 sm:mx-0 sm:h-10 sm:w-10">
                  <AlertCircle className="h-6 w-6 text-red-600" />
                </div>
                <div className="mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left">
                  <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">
                    Smazat záznam
                  </h3>
                  <div className="mt-2">
                    <p className="text-sm text-gray-500 dark:text-graphite-muted">
                      Opravdu chcete smazat záznam "
                      {entry?.title || "Bez názvu"}"? Tuto akci nelze vrátit
                      zpět.
                    </p>
                  </div>
                </div>
              </div>
            </div>
            <div className="mt-5 sm:mt-4 sm:flex sm:flex-row-reverse">
              <button
                onClick={handleDelete}
                disabled={deleteEntry.isPending}
                className="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-red-600 text-base font-medium text-white hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50"
              >
                {deleteEntry.isPending ? "Mazání..." : "Smazat"}
              </button>
              <button
                onClick={() => setShowDeleteConfirm(false)}
                className="mt-3 w-full inline-flex justify-center rounded-md border border-gray-300 dark:border-graphite-border shadow-sm dark:shadow-soft-dark px-4 py-2 bg-white dark:bg-graphite-surface text-base font-medium text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:mt-0 sm:w-auto sm:text-sm"
              >
                Zrušit
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
