import { Printer, Trash2 } from "lucide-react";
import type { PackageDto } from "../../../api/hooks/usePackages";

export type ZasilkySortBy =
  | "OrderCode"
  | "CustomerName"
  | "PackageNumber"
  | "ShippingProvider"
  | "PackedAt";

interface Props {
  items: PackageDto[];
  sortBy: ZasilkySortBy;
  sortDescending: boolean;
  onSortChange: (sortBy: ZasilkySortBy) => void;
  onReprint: (pkg: PackageDto) => void;
  onDelete: (pkg: PackageDto) => void;
}

export function ZasilkyTable({
  items,
  sortBy,
  sortDescending,
  onSortChange,
  onReprint,
  onDelete,
}: Props) {
  const indicator = (col: ZasilkySortBy) =>
    sortBy === col ? (sortDescending ? " ↓" : " ↑") : "";

  return (
    <table className="min-w-full text-base">
      <thead className="bg-slate-100 sticky top-0">
        <tr>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("OrderCode")}>
            Objednávka{indicator("OrderCode")}
          </th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("CustomerName")}>
            Zákazník{indicator("CustomerName")}
          </th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("PackageNumber")}>
            Balík{indicator("PackageNumber")}
          </th>
          <th className="px-4 py-3 text-left">Sledovací č.</th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("ShippingProvider")}>
            Dopravce{indicator("ShippingProvider")}
          </th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("PackedAt")}>
            Zabaleno{indicator("PackedAt")}
          </th>
          <th className="px-4 py-3 text-left">Zabalil</th>
          <th className="px-4 py-3 text-right">Akce</th>
        </tr>
      </thead>
      <tbody>
        {items.map((p) => (
          <tr key={p.id} className="border-t hover:bg-slate-50">
            <td className="px-4 py-3 font-mono">{p.orderCode}</td>
            <td className="px-4 py-3">{p.customerName}</td>
            <td className="px-4 py-3">{p.trackingNumber ?? p.packageNumber}</td>
            <td className="px-4 py-3 font-mono text-sm">{p.trackingNumber ?? "—"}</td>
            <td className="px-4 py-3">{p.shippingProviderName ?? p.shippingProviderCode}</td>
            <td className="px-4 py-3">{new Date(p.packedAt).toLocaleString("cs-CZ")}</td>
            <td className="px-4 py-3">{p.packedBy ?? "—"}</td>
            <td className="px-4 py-3 text-right">
              <div className="inline-flex gap-2">
                <button
                  type="button"
                  onClick={() => onReprint(p)}
                  className="inline-flex items-center gap-1 px-3 py-2 rounded bg-indigo-600 text-white"
                >
                  <Printer className="w-4 h-4" /> Tisk
                </button>
                <button
                  type="button"
                  onClick={() => onDelete(p)}
                  className="inline-flex items-center gap-1 px-3 py-2 rounded bg-red-600 text-white"
                >
                  <Trash2 className="w-4 h-4" /> Smazat
                </button>
              </div>
            </td>
          </tr>
        ))}
        {items.length === 0 && (
          <tr>
            <td className="px-4 py-8 text-center text-slate-500" colSpan={8}>
              Žádné zásilky.
            </td>
          </tr>
        )}
      </tbody>
    </table>
  );
}
