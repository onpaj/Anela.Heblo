import React from 'react';

interface ConfirmActionDialogProps {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  tone: 'danger' | 'primary';
  isPending?: boolean;
  isConfirmDisabled?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  children?: React.ReactNode;
}

const CONFIRM_TONE_CLASSES: Readonly<Record<ConfirmActionDialogProps['tone'], string>> = {
  danger: 'bg-red-600 hover:bg-red-700',
  primary: 'bg-indigo-600 hover:bg-indigo-700',
};

const TITLE_ID = 'ads-confirm-dialog-title';

const ConfirmActionDialog: React.FC<ConfirmActionDialogProps> = ({
  isOpen,
  title,
  message,
  confirmLabel,
  tone,
  isPending = false,
  isConfirmDisabled = false,
  onConfirm,
  onCancel,
  children,
}) => {
  if (!isOpen) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      <div className="fixed inset-0 bg-black bg-opacity-50 transition-opacity" onClick={onCancel} aria-hidden="true" />
      <div className="flex min-h-full items-center justify-center p-4">
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby={TITLE_ID}
          className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6"
        >
          <h3 id={TITLE_ID} className="text-lg font-semibold text-gray-900 dark:text-graphite-text mb-2">
            {title}
          </h3>
          <p className="text-sm text-gray-600 dark:text-graphite-muted">{message}</p>
          {children && <div className="mt-4">{children}</div>}
          <div className="flex gap-3 mt-6">
            <button
              type="button"
              onClick={onCancel}
              disabled={isPending}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="button"
              onClick={onConfirm}
              disabled={isPending || isConfirmDisabled}
              className={`flex-1 px-4 py-2 text-sm font-medium text-white rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${CONFIRM_TONE_CLASSES[tone]}`}
            >
              {isPending ? 'Ukládám…' : confirmLabel}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmActionDialog;
