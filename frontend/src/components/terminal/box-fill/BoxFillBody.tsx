import React from "react";
import { AlertCircle, CheckCircle2, FlaskConical, Loader, Trash2 } from "lucide-react";
import type { TerminalBox } from "../../../api/hooks/useBoxFill";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";

interface BoxFillBodyProps {
  box: TerminalBox | null;
  inventory: ManufacturedProductInventoryItem[];
  inventoryLoading: boolean;
  inventoryError: boolean;
  resumed: boolean;
  error: string | null;
  lastSentBoxCode: string | null;
  removePending: boolean;
  onSelectInventory: (item: ManufacturedProductInventoryItem) => void;
  onRemoveItem: (itemId: number) => void;
}

const BoxFillBody: React.FC<BoxFillBodyProps> = ({
  box,
  inventory,
  inventoryLoading,
  inventoryError,
  resumed,
  error,
  lastSentBoxCode,
  removePending,
  onSelectInventory,
  onRemoveItem,
}) => {
  if (!box) {
    return (
      <div className="space-y-4 pt-2">
        {lastSentBoxCode && (
          <div
            role="status"
            className="flex items-center gap-2 text-sm text-green-700 dark:text-green-300 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-900/40 rounded-lg px-3 py-2"
          >
            <CheckCircle2 className="h-4 w-4 flex-shrink-0" />
            Box <span className="font-mono font-semibold">{lastSentBoxCode}</span> byl odeslán do přepravy.
          </div>
        )}
        {error && (
          <div
            role="alert"
            className="flex items-center gap-2 text-sm text-red-600 dark:text-red-300 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-lg px-3 py-2"
          >
            <AlertCircle className="h-4 w-4 flex-shrink-0" />
            {error}
          </div>
        )}
        <p className="text-sm text-neutral-gray dark:text-graphite-muted">
          Naskenujte kód prázdného nebo rozpracovaného boxu pro zahájení plnění.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {resumed && box.items.length > 0 && (
        <div className="flex items-center gap-2 text-sm text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-900/40 rounded-lg px-3 py-2">
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          Pokračujete v rozpracovaném boxu ({box.items.length} položek).
        </div>
      )}

      {error && (
        <div
          role="alert"
          className="flex items-center gap-2 text-sm text-red-600 dark:text-red-300 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-lg px-3 py-2"
        >
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          {error}
        </div>
      )}

      {inventoryLoading && (
        <div className="flex items-center justify-center gap-2 py-6 text-sm text-neutral-gray dark:text-graphite-muted">
          <Loader className="h-4 w-4 animate-spin" /> Načítám zásoby...
        </div>
      )}
      {inventoryError && (
        <div className="flex items-center gap-2 py-4 text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4" /> Chyba při načítání zásob
        </div>
      )}
      {!inventoryLoading && !inventoryError && inventory.length === 0 && (
        <p className="text-center py-6 text-sm text-neutral-gray dark:text-graphite-muted">Žádné dostupné zásoby</p>
      )}
      {!inventoryLoading && !inventoryError && inventory.length > 0 && (
        <div className="border border-border-light dark:border-graphite-border rounded-lg divide-y divide-border-light dark:divide-graphite-border">
          {inventory.map((it) => (
            <button
              key={it.id}
              type="button"
              onClick={() => onSelectInventory(it)}
              data-testid={`inventory-row-${it.id}`}
              className="w-full text-left px-3 py-3 hover:bg-secondary-blue-pale dark:hover:bg-graphite-surface-2 active:bg-secondary-blue-pale dark:active:bg-graphite-surface-2 flex items-center gap-3"
            >
              <FlaskConical className="h-4 w-4 text-primary-blue dark:text-graphite-accent flex-shrink-0" />
              <div className="min-w-0 flex-1">
                <div className="text-sm font-medium text-neutral-slate dark:text-graphite-text truncate">{it.productName}</div>
                <div className="text-xs text-neutral-gray dark:text-graphite-muted flex flex-wrap gap-x-3">
                  <span className="font-mono">{it.productCode}</span>
                  {it.lotNumber && <span>Šarže: {it.lotNumber}</span>}
                  <span className="font-semibold text-green-700 dark:text-emerald-400">Sklad: {it.amount}</span>
                </div>
              </div>
            </button>
          ))}
        </div>
      )}

      {box.items.length > 0 && (
        <div>
          <h2 className="text-sm font-semibold text-neutral-slate dark:text-graphite-text mb-1">V boxu</h2>
          <div className="border border-border-light dark:border-graphite-border rounded-lg divide-y divide-border-light dark:divide-graphite-border">
            {box.items.map((it) => (
              <div key={it.id} className="flex items-center gap-3 px-3 py-2" data-testid={`box-item-${it.id}`}>
                <div className="min-w-0 flex-1">
                  <div className="text-sm text-neutral-slate dark:text-graphite-text truncate">{it.productName}</div>
                  <div className="text-xs text-neutral-gray dark:text-graphite-muted font-mono">
                    {it.productCode} • {it.amount}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => onRemoveItem(it.id)}
                  disabled={removePending}
                  aria-label="Odebrat položku"
                  data-testid={`remove-item-${it.id}`}
                  className="p-2 text-red-600 dark:text-red-400 rounded-md hover:bg-red-50 dark:hover:bg-red-900/20 disabled:opacity-50"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};

export default BoxFillBody;
