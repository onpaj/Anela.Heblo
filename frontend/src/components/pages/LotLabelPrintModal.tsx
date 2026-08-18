import { useEffect, useState } from "react";
import { X, Printer, Loader, ChevronDown } from "lucide-react";
import { getISOWeek, getISOWeekYear } from "date-fns";
import {
  usePrintLotLabels,
  usePrintLotCalibrationLabel,
  useFeedLotMedia,
  useLotLabelCalibration,
  useSetLotLabelCalibration,
} from "../../api/hooks/useMaterialContainers";
import { usePermissionsContext } from "../../auth/PermissionsContext";
import PrinterMediaChangeDialog from "../dialogs/PrinterMediaChangeDialog";

const MIN_COUNT = 1;
const MAX_COUNT = 200;
// Media nudge: forward-only feed (thermal printers cannot reverse). One step is a fine
// increment; buttons feed 1, 3, or 5 steps forward.
const FEED_STEP_DOTS = 4;
const FEED_STEPS = [1, 3, 5];
// Editing the printer pitch/drift calibration has its own permission: it affects every
// printed label, so it is not granted by plain material-containers write access.
const CALIBRATION_PERMISSION = "manufacture.label_calibration.write";

/** Line 1 default: ISO calendar week (2 digits) + ISO week-year (2 digits), e.g. "2926". */
export const defaultLotNumber = (date: Date = new Date()): string => {
  const week = getISOWeek(date).toString().padStart(2, "0");
  const year = (getISOWeekYear(date) % 100).toString().padStart(2, "0");
  return `${week}${year}`;
};

/** Formats a native month-input value ("YYYY-MM") to the label's "MM/YY". */
export const formatExpiration = (monthValue: string): string => {
  const match = /^(\d{4})-(\d{2})$/.exec(monthValue);
  if (!match) return "";
  const [, year, month] = match;
  return `${month}/${year.slice(2)}`;
};

interface LotLabelPrintModalProps {
  isOpen: boolean;
  onClose: () => void;
  // Predefined values (e.g. opened from a manufacture order). When absent, the modal
  // falls back to the ISO-week lot number default and an empty expiration.
  initialLotNumber?: string;
  initialExpirationMonth?: string; // native "YYYY-MM"
  initialCount?: number; // predefined label count (e.g. total manufactured items)
}

function LotLabelPrintModal({
  isOpen,
  onClose,
  initialLotNumber,
  initialExpirationMonth,
  initialCount,
}: LotLabelPrintModalProps) {
  const [lotNumber, setLotNumber] = useState("");
  const [expirationMonth, setExpirationMonth] = useState("");
  const [count, setCount] = useState(MIN_COUNT);
  const [activeTab, setActiveTab] = useState<"print" | "calibration">("print");
  const [pitchDots, setPitchDots] = useState<number | "">("");
  const [driftPer100, setDriftPer100] = useState<number | "">("");
  const [error, setError] = useState<string | null>(null);
  // Set when a print was blocked because the media type changed; holds the closure that
  // re-runs the same action confirmed. Non-null while the confirmation dialog is shown.
  const [pendingConfirm, setPendingConfirm] = useState<(() => void) | null>(null);

  const { hasPermission } = usePermissionsContext();
  const canCalibrate = hasPermission(CALIBRATION_PERMISSION);

  const printLotLabels = usePrintLotLabels();
  const printCalibration = usePrintLotCalibrationLabel();
  const feedMedia = useFeedLotMedia();
  const calibration = useLotLabelCalibration(isOpen && canCalibrate);
  const saveCalibration = useSetLotLabelCalibration();
  const isPrinting =
    printLotLabels.isPending ||
    printCalibration.isPending ||
    feedMedia.isPending;

  useEffect(() => {
    if (isOpen) {
      setLotNumber(initialLotNumber?.trim() ? initialLotNumber : defaultLotNumber());
      setExpirationMonth(initialExpirationMonth ?? "");
      setCount(
        initialCount && initialCount > 0
          ? Math.min(initialCount, MAX_COUNT)
          : MIN_COUNT,
      );
      setError(null);
      setPendingConfirm(null);
      setActiveTab("print");
    }
  }, [isOpen, initialLotNumber, initialExpirationMonth, initialCount]);

  // Populate the calibration fields once the persisted values load.
  useEffect(() => {
    if (calibration.data?.pitchDots != null) {
      setPitchDots(calibration.data.pitchDots);
    }
  }, [calibration.data?.pitchDots]);

  useEffect(() => {
    if (calibration.data?.driftDotsPer100Labels != null) {
      setDriftPer100(calibration.data.driftDotsPer100Labels);
    }
  }, [calibration.data?.driftDotsPer100Labels]);

  if (!isOpen) return null;

  const expiration = formatExpiration(expirationMonth);
  const isContentValid = lotNumber.trim().length > 0 && expiration.length > 0;
  const isValid = isContentValid && count >= MIN_COUNT && count <= MAX_COUNT;

  // When a print is blocked because the media type changed, stash a closure that re-runs
  // the same action confirmed and show the media-change dialog; otherwise clear it.
  const handleConfirmable = (
    requiresConfirmation: boolean | undefined,
    rerunConfirmed: () => void,
  ) => {
    if (requiresConfirmation) {
      setPendingConfirm(() => rerunConfirmed);
    } else {
      setPendingConfirm(null);
    }
  };

  const runPrint = (confirmed: boolean) => {
    setError(null);
    // Keep the modal open after printing so the operator can reprint / adjust.
    printLotLabels.mutate(
      { lotNumber: lotNumber.trim(), expiration, count, mediaChangeConfirmed: confirmed },
      {
        onSuccess: (res) =>
          handleConfirmable(res?.requiresMediaChangeConfirmation, () => runPrint(true)),
        onError: (err) =>
          setError(`Chyba při tisku štítků: ${(err as Error).message}`),
      },
    );
  };

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!isValid) return;
    runPrint(false);
  };

  // Prints a calibration crosshair (no content needed) without closing the modal so
  // the operator can align the round media on continuous stock, then run the batch.
  const runTestPrint = (confirmed: boolean) => {
    setError(null);
    printCalibration.mutate(
      { mediaChangeConfirmed: confirmed },
      {
        onSuccess: (res) =>
          handleConfirmable(res?.requiresMediaChangeConfirmation, () => runTestPrint(true)),
        onError: (err) =>
          setError(`Chyba při tisku štítků: ${(err as Error).message}`),
      },
    );
  };

  const handleTestPrint = () => runTestPrint(false);

  // Advances the media forward by a fixed number of dots so the operator can align
  // round stock precisely. Forward-only (thermal printers cannot feed backwards).
  const runFeed = (dots: number, confirmed: boolean) => {
    setError(null);
    feedMedia.mutate(
      { dots, mediaChangeConfirmed: confirmed },
      {
        onSuccess: (res) =>
          handleConfirmable(res?.requiresMediaChangeConfirmation, () => runFeed(dots, true)),
        onError: (err) =>
          setError(`Chyba při posunu média: ${(err as Error).message}`),
      },
    );
  };

  const handleFeed = (dots: number) => runFeed(dots, false);

  // Persists the printer calibration (pitch + drift correction, admin only) so it
  // applies to every print.
  const handleSaveCalibration = () => {
    if (pitchDots === "" || driftPer100 === "") return;
    setError(null);
    saveCalibration.mutate(
      { pitchDots, driftDotsPer100Labels: driftPer100 },
      {
        onError: (err) =>
          setError(`Chyba při uložení kalibrace: ${(err as Error).message}`),
      },
    );
  };

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
      data-testid="lot-label-print-modal"
    >
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark w-full max-w-md mx-4">
        <div className="flex items-center justify-between p-6 border-b border-gray-200 dark:border-graphite-border">
          <div className="flex items-center space-x-3">
            <Printer className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
            <h2 className="text-xl font-semibold text-gray-900 dark:text-graphite-text">
              Tisk štítků šarže
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={isPrinting}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted transition-colors"
            aria-label="Zavřít"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        <nav
          className="flex gap-6 px-6 border-b border-gray-200 dark:border-graphite-border"
          aria-label="Tabs"
        >
          {(
            [
              { id: "print", label: "Tisk" },
              { id: "calibration", label: "Kalibrace" },
            ] as const
          ).map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id)}
              className={`py-3 -mb-px border-b-2 text-sm font-medium transition-colors ${
                activeTab === tab.id
                  ? "border-indigo-600 text-indigo-600 dark:border-graphite-accent dark:text-graphite-accent"
                  : "border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700 dark:hover:text-graphite-text"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>

        <form onSubmit={handleSubmit} className="p-6">
          {error && (
            <div className="mb-4 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-900/40 text-red-700 dark:text-red-300 px-3 py-2 rounded text-sm">
              {error}
            </div>
          )}

          {activeTab === "print" && (
            <>
              <div className="mb-4">
                <label
                  htmlFor="lotNumber"
                  className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1"
                >
                  Číslo šarže *
                </label>
                <input
                  id="lotNumber"
                  type="text"
                  value={lotNumber}
                  onChange={(e) => setLotNumber(e.target.value)}
                  maxLength={8}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-graphite-border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint"
                  placeholder="např. 2926"
                  disabled={isPrinting}
                />
              </div>

              <div className="mb-4">
                <label
                  htmlFor="expiration"
                  className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1"
                >
                  Expirace *
                </label>
                <input
                  id="expiration"
                  type="month"
                  value={expirationMonth}
                  onChange={(e) => setExpirationMonth(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-graphite-border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
                  disabled={isPrinting}
                />
                {expiration && (
                  <p className="text-xs text-gray-500 dark:text-graphite-muted mt-1">
                    Na štítku: {expiration}
                  </p>
                )}
              </div>

              <div className="mb-6">
                <label
                  htmlFor="count"
                  className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1"
                >
                  Počet štítků *
                </label>
                <input
                  id="count"
                  type="number"
                  min={MIN_COUNT}
                  max={MAX_COUNT}
                  value={count}
                  onChange={(e) => setCount(Number(e.target.value))}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-graphite-border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
                  disabled={isPrinting}
                />
              </div>
            </>
          )}

          {activeTab === "calibration" && (
            <div className="mb-6">
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={handleTestPrint}
                  disabled={isPrinting}
                  title="Vytiskne kříž pro zarovnání média"
                  className="px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
                >
                  Zkušební kříž
                </button>
                {FEED_STEPS.map((steps) => (
                  <button
                    key={steps}
                    type="button"
                    onClick={() => handleFeed(FEED_STEP_DOTS * steps)}
                    disabled={isPrinting}
                    title={`Posunout médium o ${steps} vpřed`}
                    className="flex items-center gap-1 px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
                  >
                    <ChevronDown className="h-4 w-4" />+{steps}
                  </button>
                ))}
              </div>

              {canCalibrate && (
                <div className="mt-3 pt-3 border-t border-gray-200 dark:border-graphite-border space-y-3">
                  <div>
                    <label
                      htmlFor="pitchDots"
                      className="block text-xs font-medium text-gray-500 dark:text-graphite-muted mb-1"
                    >
                      Rozteč štítků (body)
                    </label>
                    <input
                      id="pitchDots"
                      type="number"
                      value={pitchDots}
                      onChange={(e) =>
                        setPitchDots(e.target.value === "" ? "" : Number(e.target.value))
                      }
                      className="w-28 px-3 py-1.5 text-sm border border-gray-300 dark:border-graphite-border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
                      disabled={saveCalibration.isPending || calibration.isLoading}
                    />
                  </div>
                  <div>
                    <label
                      htmlFor="driftPer100"
                      className="block text-xs font-medium text-gray-500 dark:text-graphite-muted mb-1"
                    >
                      Korekce driftu: celkem bodů na 100 štítků (0 = vypnuto)
                    </label>
                    <input
                      id="driftPer100"
                      type="number"
                      min={0}
                      value={driftPer100}
                      onChange={(e) =>
                        setDriftPer100(e.target.value === "" ? "" : Number(e.target.value))
                      }
                      className="w-28 px-3 py-1.5 text-sm border border-gray-300 dark:border-graphite-border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
                      disabled={saveCalibration.isPending || calibration.isLoading}
                    />
                  </div>
                  <button
                    type="button"
                    onClick={handleSaveCalibration}
                    disabled={
                      pitchDots === "" ||
                      driftPer100 === "" ||
                      saveCalibration.isPending ||
                      calibration.isLoading
                    }
                    className="px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
                  >
                    {saveCalibration.isPending ? "Ukládám…" : "Uložit kalibraci"}
                  </button>
                </div>
              )}
            </div>
          )}

          <div className="flex items-center justify-end space-x-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isPrinting}
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
            >
              Zrušit
            </button>
            {activeTab === "print" && (
              <button
                type="submit"
                disabled={!isValid || isPrinting}
                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50 flex items-center"
              >
                {printLotLabels.isPending ? (
                  <>
                    <Loader className="h-4 w-4 mr-2 animate-spin" />
                    Tisknu…
                  </>
                ) : (
                  `Vytisknout ${count}`
                )}
              </button>
            )}
          </div>
        </form>
      </div>

      <PrinterMediaChangeDialog
        isOpen={pendingConfirm !== null}
        isPending={isPrinting}
        onConfirm={() => pendingConfirm?.()}
        onCancel={() => setPendingConfirm(null)}
      />
    </div>
  );
}

export default LotLabelPrintModal;
