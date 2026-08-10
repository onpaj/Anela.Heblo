import React from 'react';
import { AlertTriangle, X } from 'lucide-react';

interface ConfirmDeleteMeetingDialogProps {
  isOpen: boolean;
  subject: string;
  isDeleting: boolean;
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

const ConfirmDeleteMeetingDialog: React.FC<ConfirmDeleteMeetingDialogProps> = ({
  isOpen,
  subject,
  isDeleting,
  error,
  onConfirm,
  onCancel,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={isDeleting ? undefined : onCancel}
      />

      {/* Dialog */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
          <button
            onClick={onCancel}
            disabled={isDeleting}
            className="absolute top-4 right-4 text-gray-400 dark:text-graphite-faint hover:text-gray-600 disabled:opacity-50"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5" />
          </button>

          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-red-100 dark:bg-red-900/30 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-red-600 dark:text-red-400" />
          </div>

          <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text text-center mb-2">
            Smazat schůzku?
          </h3>

          <p className="text-sm text-gray-600 dark:text-graphite-muted text-center mb-3">
            {`Schůzka „${subject}" bude trvale smazána včetně souhrnu, přepisu, navržených úkolů a přístupových oprávnění. Tuto akci nelze vrátit zpět.`}
          </p>

          <p className="text-sm text-gray-500 dark:text-graphite-faint text-center mb-6">
            Schůzka se už znovu nenačte z Plaudu. Úkoly, které už byly odeslány do Planneru, tam zůstanou.
          </p>

          {error && (
            <p className="text-sm text-red-600 dark:text-red-400 text-center mb-4">{error}</p>
          )}

          <div className="flex gap-3">
            <button
              onClick={onCancel}
              disabled={isDeleting}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Zrušit
            </button>
            <button
              onClick={onConfirm}
              disabled={isDeleting}
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isDeleting ? 'Mažu...' : 'Smazat'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmDeleteMeetingDialog;
