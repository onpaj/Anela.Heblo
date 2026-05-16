import React, { useState } from "react";
import { AlertCircle, CheckCircle2 } from "lucide-react";
import ScanBoxStep from "./ScanBoxStep";
import AddItemsStep from "./AddItemsStep";
import { useSendBoxToTransit, type TerminalBox } from "../../../api/hooks/useBoxFill";

type Step = "scan" | "add-items" | "done";

const BoxFillWorkflow: React.FC = () => {
  const [step, setStep] = useState<Step>("scan");
  const [box, setBox] = useState<TerminalBox | null>(null);
  const [resumed, setResumed] = useState(false);
  const [amountMemory, setAmountMemory] = useState<Record<string, number>>({});
  const [transitError, setTransitError] = useState<string | null>(null);

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
    setStep("done");
  };

  const handleNextBox = () => {
    setStep("scan");
    setBox(null);
    setResumed(false);
    setAmountMemory({});
    setTransitError(null);
  };

  if (step === "scan") {
    return <ScanBoxStep onBoxReady={handleBoxReady} />;
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

  return (
    <div className="space-y-6 pt-2 text-center">
      <CheckCircle2 className="h-16 w-16 text-green-500 mx-auto" />
      <div>
        <h1 className="text-xl font-bold text-neutral-slate">Box odeslán</h1>
        {box && (
          <p className="text-sm text-neutral-gray mt-1">
            Box{" "}
            <span className="font-mono font-semibold">{box.code}</span> byl
            odeslán do přepravy.
          </p>
        )}
      </div>
      <button
        type="button"
        onClick={handleNextBox}
        data-testid="next-box-button"
        className="w-full py-3 text-base font-semibold text-white bg-primary-blue rounded-xl"
      >
        Plnit další box
      </button>
    </div>
  );
};

export default BoxFillWorkflow;
