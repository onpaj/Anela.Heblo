import { useState } from "react";

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

  const update =
    (k: keyof FilterValues) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setLocal((prev) => ({ ...prev, [k]: e.target.value }));

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onChange(local);
  };

  return (
    <form onSubmit={handleSubmit}>
      <div className="grid grid-cols-2 md:grid-cols-6 gap-3 p-4 pb-3 bg-slate-50 border-b">
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
        <button
          type="submit"
          className="col-span-2 md:col-span-6 px-6 py-2 rounded bg-blue-600 text-white font-medium hover:bg-blue-700 active:bg-blue-800"
        >
          Hledat
        </button>
      </div>
    </form>
  );
}
