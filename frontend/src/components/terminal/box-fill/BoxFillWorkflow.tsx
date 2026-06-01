import React, { useState } from "react";
import {
  useOpenOrResumeBox, useSendBoxToTransit, useAddBoxItem, useRemoveBoxItem,
  type TerminalBox,
} from "../../../api/hooks/useBoxFill";
import {
  useManufacturedProductInventoryQuery,
  type ManufacturedProductInventoryItem,
} from "../../../api/hooks/useManufacturedProductInventory";
import { getErrorMessage } from "../../../utils/errorHandler";
import { isValidBoxCode } from "./boxCode";
import AmountEntrySheet from "./AmountEntrySheet";
import OverdraftSheet from "./OverdraftSheet";
import BoxFillBody from "./BoxFillBody";
import { ScanShell } from "../shell/ScanShell";
import { SubjectHeader } from "../shell/SubjectHeader";
import { useScanScreen } from "../shell/useScanScreen";
import type { DockAction, FlashTone } from "../shell/types";
import { useScreenView } from "../../../telemetry/useScreenView";

const BoxFillWorkflow: React.FC = () => {
  const [box, setBox] = useState<TerminalBox | null>(null);
  const [resumed, setResumed] = useState(false);
  const [amountMemory, setAmountMemory] = useState<Record<string, number>>({});
  const [lastSentBoxCode, setLastSentBoxCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<ManufacturedProductInventoryItem | null>(null);
  const [overdraft, setOverdraft] = useState<{ item: ManufacturedProductInventoryItem; amount: number } | null>(null);

  useScreenView('Terminal', 'TerminalBoxFill', box ? 'AddItemsStep' : 'ScanStep');

  const openBox = useOpenOrResumeBox();
  const sendToTransit = useSendBoxToTransit();
  const addItem = useAddBoxItem();
  const removeItem = useRemoveBoxItem();
  const { data: inv, isLoading: invLoading, error: invError } =
    useManufacturedProductInventoryQuery({ onlyWithStock: true });
  const inventory = inv?.items ?? [];

  const { flash } = useScanScreen({
    // eslint-disable-next-line @typescript-eslint/no-use-before-define
    onScan: (code) => void handleScan(code),
  });

  const handleScan = async (code: string) => {
    if (!box) {
      setError(null);
      if (!isValidBoxCode(code)) {
        setError("Neplatný kód boxu. Očekává se formát B + 3 číslice (např. B001).");
        flash('err', code);
        return;
      }
      const result = await openBox.mutateAsync(code);
      if (!result.success || !result.transportBox) {
        setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Box se nepodařilo otevřít");
        flash('err', code);
        return;
      }
      setBox(result.transportBox);
      setResumed(result.resumed ?? false);
      setLastSentBoxCode(null);
      flash(result.resumed ? 'warn' : 'ok', code);
      return;
    }
    // a box is in hand
    if (code === box.code) {
      if (box.items.length > 0) {
        // eslint-disable-next-line @typescript-eslint/no-use-before-define
        void handleTransit();
      } else {
        flash('warn', code);
      }
      return;
    }
    // product-by-scan not supported yet (deferred to spec); unknown scan
    flash('err', code);
  };

  const handleTransit = async () => {
    if (!box || box.items.length === 0 || sendToTransit.isPending) return;
    setError(null);
    const result = await sendToTransit.mutateAsync(box.id);
    if (!result.success) {
      setError("Box se nepodařilo odeslat do přepravy.");
      flash('err', box.code);
      return;
    }
    const sent = box.code;
    flash('ok', sent);
    setBox(null);
    setResumed(false);
    setAmountMemory({});
    setSelected(null);
    setOverdraft(null);
    setLastSentBoxCode(sent);
  };

  const performAdd = async (
    item: ManufacturedProductInventoryItem, amount: number,
    allowNegativeStock: boolean, tone: FlashTone,
  ) => {
    if (!box) return;
    setError(null);
    const result = await addItem.mutateAsync({
      boxId: box.id, productCode: item.productCode, productName: item.productName,
      amount, sourceInventoryId: item.id, lotNumber: item.lotNumber,
      expirationDate: item.expirationDate, allowNegativeStock,
    });
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Položku se nepodařilo přidat");
      flash('err', item.productCode);
      return;
    }
    setBox(result.transportBox);
    setAmountMemory((prev) => ({ ...prev, [item.productCode]: amount }));
    setSelected(null);
    setOverdraft(null);
    flash(tone, item.productCode);
  };

  const handleAmountConfirm = (amount: number) => {
    if (!selected) return;
    if (amount > selected.amount) { setOverdraft({ item: selected, amount }); setSelected(null); return; }
    void performAdd(selected, amount, false, 'ok');
  };

  const handleRemove = async (itemId: number) => {
    if (!box) return;
    setError(null);
    const result = await removeItem.mutateAsync({ boxId: box.id, itemId });
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Položku se nepodařilo odebrat");
      return;
    }
    setBox(result.transportBox);
  };

  const subject = box
    ? <SubjectHeader code={box.code} state={box.state} facts={`${box.items.length} položek`} />
    : <SubjectHeader emptyPrompt="Naskenujte box k naplnění" />;

  const actions: DockAction[] = box ? [
    { label: 'Odeslat do přepravy', onClick: () => void handleTransit(),
      disabled: box.items.length === 0 || sendToTransit.isPending,
      loading: sendToTransit.isPending, testId: 'proceed-to-transit' },
  ] : [];

  return (
    <ScanShell subject={subject} actions={actions}>
      <BoxFillBody
        box={box}
        inventory={inventory}
        inventoryLoading={invLoading}
        inventoryError={!!invError}
        resumed={resumed}
        error={error}
        lastSentBoxCode={lastSentBoxCode}
        removePending={removeItem.isPending}
        onSelectInventory={(it) => { setError(null); setSelected(it); }}
        onRemoveItem={(id) => void handleRemove(id)}
      />
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
          onAddNegative={() => void performAdd(overdraft.item, overdraft.amount, true, 'warn')}
          onAddRemaining={() => void performAdd(overdraft.item, overdraft.item.amount, false, 'warn')}
          onCancel={() => setOverdraft(null)}
        />
      )}
    </ScanShell>
  );
};

export default BoxFillWorkflow;
