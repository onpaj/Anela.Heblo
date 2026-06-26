import React, { useState } from "react";
import { ScanLine, ArrowRight, AlertCircle } from "lucide-react";
import { useOpenOrResumeBox } from "../../../api/hooks/useBoxFill";
import { isValidBoxCode } from "../../terminal/box-fill/boxCode";

interface OpenBoxByCodeFieldProps {
  onOpenBox: (boxId: number) => void;
}

const INVALID_CODE_MESSAGE =
  "Neplatný kód boxu. Očekává se formát B + 3 číslice (např. B001).";
const OPEN_FAILED_MESSAGE = "Box se nepodařilo otevřít. Zkuste to znovu.";

// Large code-entry field: type or scan the B### label from the physical box.
// One submit either resumes the existing open box or creates+opens a new one
// (backend OpenOrResumeBoxByCode), then hands the box id up to open its detail.
const OpenBoxByCodeField: React.FC<OpenBoxByCodeFieldProps> = ({
  onOpenBox,
}) => {
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const openBox = useOpenOrResumeBox();

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();

    const trimmed = code.trim().toUpperCase();
    if (!isValidBoxCode(trimmed)) {
      setError(INVALID_CODE_MESSAGE);
      return;
    }

    setError(null);
    try {
      const result = await openBox.mutateAsync(trimmed);
      if (result.success && result.transportBox?.id) {
        setCode("");
        onOpenBox(result.transportBox.id);
      } else {
        setError(OPEN_FAILED_MESSAGE);
      }
    } catch {
      setError(OPEN_FAILED_MESSAGE);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-2">
      <label
        htmlFor="open-box-code"
        className="block text-sm font-medium text-gray-700 dark:text-graphite-muted"
      >
        Otevřít box
      </label>
      <div className="flex gap-2">
        <div className="relative flex-1">
          <ScanLine className="pointer-events-none absolute left-3 top-1/2 h-5 w-5 -translate-y-1/2 text-gray-400 dark:text-graphite-faint" />
          <input
            id="open-box-code"
            type="text"
            inputMode="text"
            autoComplete="off"
            autoCapitalize="characters"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            disabled={openBox.isPending}
            placeholder="Naskenujte nebo zadejte kód (B001)"
            className="h-14 w-full rounded-xl border border-gray-300 pl-11 pr-3 text-lg uppercase focus:border-transparent focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint"
          />
        </div>
        <button
          type="submit"
          disabled={openBox.isPending || code.trim() === ""}
          className="flex h-14 items-center justify-center gap-2 rounded-xl bg-indigo-600 px-6 text-base font-medium text-white transition-colors hover:bg-indigo-700 disabled:opacity-50"
        >
          {openBox.isPending ? "Otevírám…" : "Otevřít"}
          {!openBox.isPending && <ArrowRight className="h-5 w-5" />}
        </button>
      </div>

      {error && (
        <p className="flex items-center gap-1 text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          {error}
        </p>
      )}
    </form>
  );
};

export default OpenBoxByCodeField;
