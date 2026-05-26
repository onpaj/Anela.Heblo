import React, { useState } from "react";
import { AlertCircle } from "lucide-react";
import ScanBoxStep from "./ScanBoxStep";
import AddItemsStep from "./AddItemsStep";
import { useSendBoxToTransit, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { useScreenView } from '../../../telemetry/useScreenView';

type Step = "scan" | "add-items";

const BoxFillWorkflow: React.FC = () => {
  const [step, setStep] = useState<Step>("scan");
  const [box, setBox] = useState<TerminalBox | null>(null);
  const [resumed, setResumed] = useState(false);
  const [amountMemory, setAmountMemory] = useState<Record<string, number>>({});
  const [transitError, setTransitError] = useState<string | null>(null);
  const [lastSentBoxCode, setLastSentBoxCode] = useState<string | null>(null);
  useScreenView('Terminal', 'TerminalBoxFill', step === 'scan' ? 'ScanStep' : 'AddItemsStep');

  const sendToTransit = useSendBoxToTransit();

  const handleBoxReady = (b: TerminalBox, r: boolean) => {
    setBox(b);
    setResumed(r);
    setStep("add-items");
  };

  const handleBoxUpdated = (b: TerminalBox) => setBox(b);

  const handleAmountUsed = (productCode: string, amount: number) => {
    setAmountMemory((prev) => ({ ...prev, [productCode]: amount }));
  };

  const handleTransit = async () => {
    if (!box) return;
    setTransitError(null);
    const result = await sendToTransit.mutateAsync(box.id);
    if (!result.success) {
      setTransitError("Box se nepodařilo odeslat do přepravy.");
      return;
    }
    resetToScan(box.code);
  };

  const resetToScan = (sentBoxCode: string | null = null) => {
    setStep("scan");
    setBox(null);
    setResumed(false);
    setAmountMemory({});
    setTransitError(null);
    setLastSentBoxCode(sentBoxCode);
  };

  if (step === "scan") {
    return <ScanBoxStep onBoxReady={handleBoxReady} lastSentBoxCode={lastSentBoxCode} />;
  }

  if (step === "add-items" && box) {
    return (
      <>
        {transitError && (
          <div
            role="alert"
            className="flex items-center gap-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2 mb-4"
          >
            <AlertCircle className="h-4 w-4 flex-shrink-0" />
            {transitError}
          </div>
        )}
        <AddItemsStep
          box={box}
          resumed={resumed}
          amountMemory={amountMemory}
          onBoxUpdated={handleBoxUpdated}
          onAmountUsed={handleAmountUsed}
          onProceed={() => void handleTransit()}
          isTransiting={sendToTransit.isPending}
        />
      </>
    );
  }

  return null;
};

export default BoxFillWorkflow;
