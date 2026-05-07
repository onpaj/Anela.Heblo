import React from 'react';
import { AlertTriangle, X } from 'lucide-react';

interface Props {
  isOpen: boolean;
  tagName: string;
  assignmentCount: number;
  onConfirm: () => void;
  onCancel: () => void;
}

const ConfirmDeleteTagDialog: React.FC<Props> = ({
  isOpen,
  tagName,
  assignmentCount,
  onConfirm,
  onCancel,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={onCancel}
      />

      {/* Dialog */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white rounded-lg shadow-xl max-w-md w-full p-6">
          {/* Close button */}
          <button
            onClick={onCancel}
            className="absolute top-4 right-4 text-gray-400 hover:text-gray-600"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5" />
          </button>

          {/* Icon */}
          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-yellow-100 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-yellow-600" />
          </div>

          {/* Title */}
          <h3 className="text-lg font-semibold text-gray-900 text-center mb-2">
            Smazat štítek?
          </h3>

          {/* Message */}
          <p className="text-sm text-gray-600 text-center mb-6">
            {`Štítek „${tagName}" je přiřazen k ${assignmentCount} fotografiím. Smazáním ho odstraníte ze všech fotografií. Pokračovat?`}
          </p>

          {/* Actions */}
          <div className="flex gap-3">
            <button
              onClick={onCancel}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition-colors"
            >
              Zrušit
            </button>
            <button
              onClick={onConfirm}
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 transition-colors"
            >
              Smazat
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmDeleteTagDialog;
