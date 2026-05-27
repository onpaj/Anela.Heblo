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
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full p-6">
        <div className="flex items-start gap-3">
          <AlertTriangle className="w-6 h-6 text-red-600 flex-shrink-0" />
          <div>
            <h3 className="text-lg font-semibold">Smazat zásilku?</h3>
            <p className="mt-2 text-slate-600">
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
            className="px-4 py-2 rounded border"
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
