import React, { useState } from "react";
import { AlertCircle, CheckCircle2 } from "lucide-react";
import ScanBoxStep from "./ScanBoxStep";
import AddItemsStep from "./AddItemsStep";
import ScanInput from "../ScanInput";
import { useSendBoxToTransit, type TerminalBox } from "../../../api/hooks/useBoxFill";

type Step = "scan" | "add-items" | "confirm-transit" | "done";

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

  const handleConfirmTransit = async (scannedCode: string) => {
    if (!box) return;
    if (scannedCode !== box.code) {
      setTransitError(`Kód boxu se neshoduje. Očekává se ${box.code}.`);
      return;
    }
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
      <AddItemsStep
        box={box}
        resumed={resumed}
        amountMemory={amountMemory}
        onBoxUpdated={handleBoxUpdated}
        onAmountUsed={handleAmountUsed}
        onProceed={() => setStep("confirm-transit")}
      />
    );
  }

  if (step === "confirm-transit" && box) {
    return (
      <div className="space-y-4 pt-2">
        <h1 className="text-xl font-bold text-neutral-slate">Potvrďte odeslání</h1>
        <p className="text-sm text-neutral-gray">
          Naskenujte kód boxu{" "}
          <span className="font-mono font-semibold">{box.code}</span> pro
          potvrzení odeslání do přepravy.
        </p>
        <ScanInput
          label="Kód boxu"
          onScan={(v) => void handleConfirmTransit(v)}
          loading={sendToTransit.isPending}
          suppressKeyboard
          allowKeyboardToggle
        />
        {transitError && (
          <div
            role="alert"
            className="flex items-center gap-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
          >
            <AlertCircle className="h-4 w-4 flex-shrink-0" />
            {transitError}
          </div>
        )}
      </div>
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
