import React from 'react';
import { AlertTriangle, X } from 'lucide-react';

interface ConfirmTriggerJobDialogProps {
  isOpen: boolean;
  jobName: string;
  jobDisplayName: string;
  isJobDisabled: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

const ConfirmTriggerJobDialog: React.FC<ConfirmTriggerJobDialogProps> = ({
  isOpen,
  jobName,
  jobDisplayName,
  isJobDisabled,
  onConfirm,
  onCancel
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
          >
            <X className="h-5 w-5" />
          </button>

          {/* Icon */}
          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-yellow-100 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-yellow-600" />
          </div>

          {/* Title */}
          <h3 className="text-lg font-semibold text-gray-900 text-center mb-2">
            Spustit úlohu nyní?
          </h3>

          {/* Message */}
          <div className="text-sm text-gray-600 text-center mb-6">
            <p className="mb-2">
              Chystáte se manuálně spustit úlohu:
            </p>
            <p className="font-semibold text-gray-900">{jobDisplayName}</p>
            <p className="text-xs text-gray-500 mt-1">({jobName})</p>

            {isJobDisabled && (
              <div className="mt-4 p-3 bg-yellow-50 border border-yellow-200 rounded-md">
                <p className="text-yellow-800 font-medium">
                  ⚠️ Úloha je aktuálně vypnutá
                </p>
                <p className="text-yellow-700 text-xs mt-1">
                  Spuštěním potvrdíte, že chcete tuto úlohu spustit i když je vypnutá.
                </p>
              </div>
            )}
          </div>

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
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 transition-colors"
            >
              Spustit
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmTriggerJobDialog;
