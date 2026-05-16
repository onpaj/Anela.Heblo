import React, { useState } from "react";
import { AlertCircle, Loader } from "lucide-react";
import TerminalScanInput from "../TerminalScanInput";
import { useOpenOrResumeBox, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { isValidBoxCode } from "./boxCode";
import { getErrorMessage } from "../../../utils/errorHandler";

interface ScanBoxStepProps {
  onBoxReady: (box: TerminalBox, resumed: boolean) => void;
}

const ScanBoxStep: React.FC<ScanBoxStepProps> = ({ onBoxReady }) => {
  const [error, setError] = useState<string | null>(null);
  const openBox = useOpenOrResumeBox();

  const handleScan = async (code: string) => {
    setError(null);
    if (!isValidBoxCode(code)) {
      setError("Neplatný kód boxu. Očekává se formát B + 3 číslice (např. B001).");
      return;
    }
    const result = await openBox.mutateAsync(code);
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Box se nepodařilo otevřít");
      return;
    }
    onBoxReady(result.transportBox, result.resumed ?? false);
  };

  return (
    <div className="space-y-4 pt-2">
      <h1 className="text-xl font-bold text-neutral-slate">Naskenujte box</h1>
      <p className="text-sm text-neutral-gray">
        Naskenujte kód prázdného nebo rozpracovaného boxu pro zahájení plnění.
      </p>
      <TerminalScanInput
        label="Kód boxu"
        placeholder="B001"
        onScan={(v) => void handleScan(v)}
        disabled={openBox.isPending}
      />
      {openBox.isPending && (
        <div className="flex items-center gap-2 text-sm text-neutral-gray">
          <Loader className="h-4 w-4 animate-spin" /> Otevírám box...
        </div>
      )}
      {error && (
        <div
          role="alert"
          className="flex items-center gap-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
        >
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          {error}
        </div>
      )}
    </div>
  );
};

export default ScanBoxStep;
