import { AlertTriangle } from "lucide-react";
import type { PackageDto } from "../../../api/hooks/usePackages";

interface Props {
  pkg: PackageDto | null;
  isDeleting: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function DeletePackageDialog({ pkg, isDeleting, onConfirm, onCancel }: Props) {
  if (!pkg) return null;
  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
        <div className="flex items-start gap-3">
          <AlertTriangle className="w-6 h-6 text-red-600 dark:text-red-400 flex-shrink-0" />
          <div>
            <h3 className="text-lg font-semibold dark:text-graphite-text">Smazat zásilku?</h3>
            <p className="mt-2 text-slate-600 dark:text-graphite-muted">
              Smaže zásilku <strong>{pkg.packageNumber}</strong> pro objednávku{" "}
              <strong>{pkg.orderCode}</strong> a zruší ji v Shoptetu. Akci nelze vrátit.
            </p>
          </div>
        </div>
        <div className="mt-6 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={isDeleting}
            className="px-4 py-2 rounded border dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5"
          >
            Zrušit
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={isDeleting}
            className="px-4 py-2 rounded border bg-red-600 text-white disabled:opacity-50"
          >
            {isDeleting ? "Maže se..." : "Smazat"}
          </button>
        </div>
      </div>
    </div>
  );
}
