import { useState } from 'react';
import { Minus, Plus, X } from 'lucide-react';
import ScanInput from '../terminal/ScanInput';

interface MultiPackageModalProps {
  onConfirm: (orderCode: string, packageCount: number) => void;
  onClose: () => void;
}

const MIN_PACKAGES = 2;
const MAX_PACKAGES = 10;
const DEFAULT_PACKAGES = 2;

function MultiPackageModal({ onConfirm, onClose }: MultiPackageModalProps) {
  const [count, setCount] = useState(DEFAULT_PACKAGES);

  return (
    <div
      data-testid="multi-package-modal"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6"
    >
      <div className="flex w-full max-w-sm flex-col gap-6 rounded-2xl bg-white dark:bg-graphite-surface p-8 shadow-2xl">
        <div className="flex items-center justify-between">
          <h2 className="text-2xl font-bold text-neutral-slate dark:text-graphite-text">Více balíků</h2>
          <button
            type="button"
            aria-label="Zavřít"
            data-testid="multi-package-close"
            onClick={onClose}
            className="rounded-lg p-2 text-neutral-gray dark:text-graphite-muted hover:bg-neutral-100 dark:hover:bg-graphite-surface-2"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        <div className="flex items-center justify-center gap-6">
          <button
            type="button"
            aria-label="Méně balíků"
            data-testid="multi-package-decrement"
            disabled={count <= MIN_PACKAGES}
            onClick={() => setCount((c) => Math.max(MIN_PACKAGES, c - 1))}
            className="flex h-16 w-16 items-center justify-center rounded-2xl border-2 border-neutral-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-neutral-slate dark:text-graphite-text shadow dark:shadow-soft-dark active:scale-95 disabled:opacity-40"
          >
            <Minus className="h-8 w-8" />
          </button>
          <span
            data-testid="multi-package-count"
            className="w-16 text-center text-5xl font-bold text-neutral-slate dark:text-graphite-text"
          >
            {count}
          </span>
          <button
            type="button"
            aria-label="Více balíků"
            data-testid="multi-package-increment"
            disabled={count >= MAX_PACKAGES}
            onClick={() => setCount((c) => Math.min(MAX_PACKAGES, c + 1))}
            className="flex h-16 w-16 items-center justify-center rounded-2xl bg-primary-blue text-white shadow active:scale-95 disabled:opacity-40"
          >
            <Plus className="h-8 w-8" />
          </button>
        </div>

        <ScanInput
          label="Potvrďte naskenováním objednávky"
          placeholder="Naskenujte objednávku…"
          onScan={(orderCode) => onConfirm(orderCode, count)}
          autoFocusOnMount
          refocusOnBlur
        />
      </div>
    </div>
  );
}

export default MultiPackageModal;
