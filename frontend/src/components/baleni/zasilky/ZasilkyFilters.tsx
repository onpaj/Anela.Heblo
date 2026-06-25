import { useState } from "react";
import { Carriers } from "../../../api/generated/api-client";
import { CARRIER_LABELS } from "../../../constants/carrierLabels";

export interface FilterValues {
  orderCode: string;
  customerName: string;
  packageNumber: string;
  carrier: string;
  date: string;
}

interface Props {
  value: FilterValues;
  onChange: (value: FilterValues) => void;
}

const EMPTY_FILTERS: FilterValues = {
  orderCode: "",
  customerName: "",
  packageNumber: "",
  carrier: "",
  date: "",
};

export function ZasilkyFilters({ value, onChange }: Props) {
  const [local, setLocal] = useState<FilterValues>(value);

  const update =
    (k: keyof FilterValues) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
      setLocal((prev) => ({ ...prev, [k]: e.target.value }));

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onChange(local);
  };

  const handleClear = () => {
    setLocal(EMPTY_FILTERS);
    onChange(EMPTY_FILTERS);
  };

  return (
    <form aria-label="Filtry zásilek" onSubmit={handleSubmit}>
      <div className="grid grid-cols-2 md:grid-cols-6 gap-3 p-4 bg-slate-50 dark:bg-graphite-surface-2 border-b dark:border-graphite-border">
        <input
          className="px-3 py-2 border rounded dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:placeholder-graphite-faint"
          placeholder="Objednávka"
          value={local.orderCode}
          onChange={update("orderCode")}
        />
        <input
          className="px-3 py-2 border rounded dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:placeholder-graphite-faint"
          placeholder="Zákazník"
          value={local.customerName}
          onChange={update("customerName")}
        />
        <input
          className="px-3 py-2 border rounded dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:placeholder-graphite-faint"
          placeholder="Číslo balíku"
          value={local.packageNumber}
          onChange={update("packageNumber")}
        />
        <select
          className="px-3 py-2 border rounded bg-white dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text"
          value={local.carrier}
          onChange={update("carrier")}
        >
          <option value="">Všichni dopravci</option>
          {(Object.entries(CARRIER_LABELS) as [Carriers, string][]).map(
            ([code, label]) => (
              <option key={code} value={code}>
                {label}
              </option>
            ),
          )}
        </select>
        <input
          type="date"
          className="px-3 py-2 border rounded dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text"
          value={local.date}
          onChange={update("date")}
        />
        <div className="col-span-2 md:col-span-1 flex gap-2">
          <button
            type="submit"
            className="flex-1 px-4 py-2 rounded bg-blue-600 text-white font-medium hover:bg-blue-700 active:bg-blue-800"
          >
            Hledat
          </button>
          <button
            type="button"
            onClick={handleClear}
            className="flex-1 px-4 py-2 rounded border border-slate-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-slate-700 dark:text-graphite-muted font-medium hover:bg-slate-50 dark:hover:bg-white/5 active:bg-slate-100"
          >
            Vymazat
          </button>
        </div>
      </div>
    </form>
  );
}
