import React, { useState } from "react";
import { AlertCircle, FlaskConical, Loader, Trash2 } from "lucide-react";
import {
  useManufacturedProductInventoryQuery,
  type ManufacturedProductInventoryItem,
} from "../../../api/hooks/useManufacturedProductInventory";
import { useAddBoxItem, useRemoveBoxItem, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { getErrorMessage } from "../../../utils/errorHandler";
import ScanInput from "../ScanInput";
import AmountEntrySheet from "./AmountEntrySheet";
import OverdraftSheet from "./OverdraftSheet";

interface AddItemsStepProps {
  box: TerminalBox;
  resumed: boolean;
  amountMemory: Record<string, number>;
  onBoxUpdated: (box: TerminalBox) => void;
  onAmountUsed: (productCode: string, amount: number) => void;
  onProceed: () => void;
  isTransiting?: boolean;
}

const AddItemsStep: React.FC<AddItemsStepProps> = ({
  box,
  resumed,
  amountMemory,
  onBoxUpdated,
  onAmountUsed,
  onProceed,
  isTransiting = false,
}) => {
  const [selected, setSelected] = useState<ManufacturedProductInventoryItem | null>(null);
  const [overdraft, setOverdraft] = useState<{ item: ManufacturedProductInventoryItem; amount: number } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const { data, isLoading, error: loadError } = useManufacturedProductInventoryQuery({ onlyWithStock: true });
  const addItem = useAddBoxItem();
  const removeItem = useRemoveBoxItem();

  const items = data?.items ?? [];

  const performAdd = async (
    item: ManufacturedProductInventoryItem,
    amount: number,
    allowNegativeStock: boolean,
  ) => {
    setError(null);
    const result = await addItem.mutateAsync({
      boxId: box.id,
      productCode: item.productCode,
      productName: item.productName,
      amount,
      sourceInventoryId: item.id,
      lotNumber: item.lotNumber,
      expirationDate: item.expirationDate,
      allowNegativeStock,
    });
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Položku se nepodařilo přidat");
      return;
    }
    onBoxUpdated(result.transportBox);
    onAmountUsed(item.productCode, amount);
    setSelected(null);
    setOverdraft(null);
  };

  const handleAmountConfirm = (amount: number) => {
    if (!selected) return;
    if (amount > selected.amount) {
      setOverdraft({ item: selected, amount });
      setSelected(null);
      return;
    }
    void performAdd(selected, amount, false);
  };

  const handleRemove = async (itemId: number) => {
    setError(null);
    const result = await removeItem.mutateAsync({ boxId: box.id, itemId });
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Položku se nepodařilo odebrat");
      return;
    }
    onBoxUpdated(result.transportBox);
  };

  return (
    <div className="space-y-4">
      <ScanInput
        label={`Naskenujte kód boxu ${box.code}`}
        placeholder={box.code}
        onScan={(code) => { if (code === box.code && box.items.length > 0) onProceed(); }}
        loading={isTransiting}
        autoFocusOnMount={false}
        refocusOnBlur={false}
        suppressKeyboard
        allowKeyboardToggle
      />

      <div className="flex items-center justify-between">
        <h1 className="text-xl font-bold text-neutral-slate dark:text-graphite-text">Box {box.code}</h1>
        <span className="text-sm text-neutral-gray dark:text-graphite-muted">{box.items.length} pol.</span>
      </div>

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

      {isLoading && (
        <div className="flex items-center justify-center gap-2 py-6 text-sm text-neutral-gray dark:text-graphite-muted">
          <Loader className="h-4 w-4 animate-spin" /> Načítám zásoby...
        </div>
      )}
      {loadError && (
        <div className="flex items-center gap-2 py-4 text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4" /> Chyba při načítání zásob
        </div>
      )}
      {!isLoading && !loadError && items.length === 0 && (
        <p className="text-center py-6 text-sm text-neutral-gray dark:text-graphite-muted">Žádné dostupné zásoby</p>
      )}
      {!isLoading && !loadError && items.length > 0 && (
        <div className="border border-border-light dark:border-graphite-border rounded-lg divide-y divide-border-light dark:divide-graphite-border">
          {items.map((it) => (
            <button
              key={it.id}
              type="button"
              onClick={() => {
                setError(null);
                setSelected(it);
              }}
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
                  onClick={() => void handleRemove(it.id)}
                  disabled={removeItem.isPending}
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

      <button
        type="button"
        onClick={onProceed}
        disabled={box.items.length === 0 || isTransiting}
        data-testid="proceed-to-transit"
        className="w-full py-3 text-base font-semibold text-white bg-primary-blue rounded-xl disabled:opacity-50 flex items-center justify-center gap-2"
      >
        {isTransiting && <Loader className="h-4 w-4 animate-spin" />}
        Odeslat do přepravy
      </button>

      {selected && (
        <AmountEntrySheet
          item={selected}
          initialAmount={amountMemory[selected.productCode]}
          isSubmitting={addItem.isPending}
          onConfirm={handleAmountConfirm}
          onCancel={() => setSelected(null)}
        />
      )}
      {overdraft && (
        <OverdraftSheet
          item={overdraft.item}
          requestedAmount={overdraft.amount}
          isSubmitting={addItem.isPending}
          onAddNegative={() => void performAdd(overdraft.item, overdraft.amount, true)}
          onAddRemaining={() => void performAdd(overdraft.item, overdraft.item.amount, false)}
          onCancel={() => setOverdraft(null)}
        />
      )}
    </div>
  );
};

export default AddItemsStep;
