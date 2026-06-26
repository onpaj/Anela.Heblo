import React, { useState } from "react";
import { AlertCircle, CheckCircle2 } from "lucide-react";
import ScanInput from "../ScanInput";
import { useOpenOrResumeBox, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { isValidBoxCode } from "./boxCode";
import { getErrorMessage } from "../../../utils/errorHandler";

interface ScanBoxStepProps {
  onBoxReady: (box: TerminalBox, resumed: boolean) => void;
  lastSentBoxCode?: string | null;
}

const ScanBoxStep: React.FC<ScanBoxStepProps> = ({ onBoxReady, lastSentBoxCode }) => {
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
      {lastSentBoxCode && (
        <div
          role="status"
          className="flex items-center gap-2 text-sm text-green-700 dark:text-green-300 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-900/40 rounded-lg px-3 py-2"
        >
          <CheckCircle2 className="h-4 w-4 flex-shrink-0" />
          Box <span className="font-mono font-semibold">{lastSentBoxCode}</span> byl odeslán do přepravy.
        </div>
      )}
      <ScanInput
        label="Kód boxu"
        placeholder="B001"
        onScan={(v) => void handleScan(v)}
        loading={openBox.isPending}
        suppressKeyboard
        allowKeyboardToggle
      />
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
};

export default ScanBoxStep;
