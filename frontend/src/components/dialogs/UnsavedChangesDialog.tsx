import React from "react";
import { AlertTriangle, X } from "lucide-react";

interface UnsavedChangesDialogProps {
  isOpen: boolean;
  isSaving: boolean;
  onSave: () => void;
  onDiscard: () => void;
  onKeepEditing: () => void;
}

const UnsavedChangesDialog: React.FC<UnsavedChangesDialogProps> = ({
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
        <div className="relative bg-white rounded-lg shadow-xl max-w-md w-full p-6">
          {/* Close button */}
          <button
            onClick={onKeepEditing}
            className="absolute top-4 right-4 text-gray-400 hover:text-gray-600"
            aria-label="Close"
          >
            <X className="h-5 w-5" />
          </button>

          {/* Icon */}
          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-yellow-100 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-yellow-600" />
          </div>

          {/* Title */}
          <h3 className="text-lg font-semibold text-gray-900 text-center mb-2">
            Unsaved changes
          </h3>

          {/* Message */}
          <div className="text-sm text-gray-600 text-center mb-6">
            <p>You have unsaved changes. Save them before leaving?</p>
          </div>

          {/* Actions */}
          <div className="flex gap-3">
            <button
              onClick={onKeepEditing}
              disabled={isSaving}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition-colors disabled:opacity-50"
            >
              Keep editing
            </button>
            <button
              onClick={onDiscard}
              disabled={isSaving}
              className="flex-1 px-4 py-2 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-md hover:bg-red-50 transition-colors disabled:opacity-50"
            >
              Discard changes
            </button>
            <button
              onClick={onSave}
              disabled={isSaving}
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 transition-colors disabled:opacity-50"
            >
              {isSaving ? "Saving…" : "Save changes"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default UnsavedChangesDialog;
