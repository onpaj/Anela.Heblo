import { useEffect, useState } from "react";

export interface FilterValues {
  orderCode: string;
  customerName: string;
  packageNumber: string;
  shippingProviderCode: string;
  fromDate: string;
  toDate: string;
}

interface Props {
  value: FilterValues;
  onChange: (value: FilterValues) => void;
}

export function ZasilkyFilters({ value, onChange }: Props) {
  const [local, setLocal] = useState<FilterValues>(value);

  useEffect(() => {
    const t = setTimeout(() => onChange(local), 300);
    return () => clearTimeout(t);
  }, [local, onChange]);

  const update =
    (k: keyof FilterValues) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setLocal({ ...local, [k]: e.target.value });

  return (
    <div className="grid grid-cols-2 md:grid-cols-6 gap-3 p-4 bg-slate-50 border-b">
      <input
        className="px-3 py-2 border rounded"
        placeholder="Objednávka"
        value={local.orderCode}
        onChange={update("orderCode")}
      />
      <input
        className="px-3 py-2 border rounded"
        placeholder="Zákazník"
        value={local.customerName}
        onChange={update("customerName")}
      />
      <input
        className="px-3 py-2 border rounded"
        placeholder="Číslo balíku"
        value={local.packageNumber}
        onChange={update("packageNumber")}
      />
      <input
        className="px-3 py-2 border rounded"
        placeholder="Dopravce (kód)"
        value={local.shippingProviderCode}
        onChange={update("shippingProviderCode")}
      />
      <input
        type="date"
        className="px-3 py-2 border rounded"
        value={local.fromDate}
        onChange={update("fromDate")}
      />
      <input
        type="date"
        className="px-3 py-2 border rounded"
        value={local.toDate}
        onChange={update("toDate")}
      />
    </div>
  );
}
