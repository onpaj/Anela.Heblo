import React from "react";
import { AlertTriangle, X, Loader } from "lucide-react";

interface PrinterMediaChangeDialogProps {
  isOpen: boolean;
  isPending?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Shown when the operator prints a different label type than last time on the shared Zebra
 * printer. Reminds them to swap and calibrate the media roll before the print proceeds.
 */
const PrinterMediaChangeDialog: React.FC<PrinterMediaChangeDialogProps> = ({
  isOpen,
  isPending = false,
  onConfirm,
  onCancel,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[60] overflow-y-auto" data-testid="printer-media-change-dialog">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={isPending ? undefined : onCancel}
      />

      {/* Dialog */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
          {/* Close button */}
          <button
            type="button"
            onClick={onCancel}
            disabled={isPending}
            aria-label="Zavřít"
            className="absolute top-4 right-4 text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted disabled:opacity-50"
          >
            <X className="h-5 w-5" />
          </button>

          {/* Icon */}
          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-yellow-100 dark:bg-amber-900/30 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-yellow-600 dark:text-amber-400" />
          </div>

          {/* Title */}
          <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text text-center mb-2">
            Výměna média
          </h3>

          {/* Message */}
          <div className="text-sm text-gray-600 dark:text-graphite-muted text-center mb-6">
            <p>
              Chystáte se tisknout jiný typ štítků než minule. Ujistěte se, že jste v tiskárně
              vyměnili a zkalibrovali médium, jinak se štítky vytisknou špatně.
            </p>
          </div>

          {/* Actions */}
          <div className="flex gap-3">
            <button
              type="button"
              onClick={onCancel}
              disabled={isPending}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="button"
              onClick={onConfirm}
              disabled={isPending}
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 transition-colors disabled:opacity-50 flex items-center justify-center"
            >
              {isPending ? (
                <>
                  <Loader className="h-4 w-4 mr-2 animate-spin" />
                  Tisknu…
                </>
              ) : (
                "Pokračovat v tisku"
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default PrinterMediaChangeDialog;
