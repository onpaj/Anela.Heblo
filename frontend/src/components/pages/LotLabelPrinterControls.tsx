import { useState } from "react";
import {
  ChevronDown,
  ChevronUp,
  ChevronsDown,
  ChevronsUp,
  CheckCircle2,
  AlertTriangle,
} from "lucide-react";
import {
  LabelDriftDirection,
  LabelDriftSpeed,
} from "../../api/generated/api-client";
import { useNudgeLotLabelCalibration } from "../../api/hooks/useMaterialContainers";

// Media nudge: forward-only feed (thermal printers cannot reverse). One step is a fine
// increment; buttons feed 1, 3, or 5 steps forward.
const FEED_STEP_DOTS = 4;
const FEED_STEPS = [1, 3, 5];

// The four observations an operator can report. The reference label counts are what give
// "rychle" and "pomalu" a single meaning regardless of how long the batch was.
const DRIFT_OPTIONS = [
  {
    direction: LabelDriftDirection.Up,
    speed: LabelDriftSpeed.Fast,
    label: "Nahoru rychle",
    hint: "během ~20 štítků",
    Icon: ChevronsUp,
  },
  {
    direction: LabelDriftDirection.Up,
    speed: LabelDriftSpeed.Slow,
    label: "Nahoru pomalu",
    hint: "až po ~50 štítcích",
    Icon: ChevronUp,
  },
  {
    direction: LabelDriftDirection.Down,
    speed: LabelDriftSpeed.Fast,
    label: "Dolů rychle",
    hint: "během ~20 štítků",
    Icon: ChevronsDown,
  },
  {
    direction: LabelDriftDirection.Down,
    speed: LabelDriftSpeed.Slow,
    label: "Dolů pomalu",
    hint: "až po ~50 štítcích",
    Icon: ChevronDown,
  },
] as const;

interface LotLabelPrinterControlsProps {
  // A print, media feed or nudge is in flight anywhere in the modal. The parent owns all
  // of them: prints route through the media-change dialog, and the nudge must also block
  // the batch print that would otherwise consume a calibration still being written.
  isBusy: boolean;
  nudgeCalibration: ReturnType<typeof useNudgeLotLabelCalibration>;
  onTestPrint: () => void;
  onFeed: (dots: number) => void;
  // Reported into the modal's shared error banner rather than rendered twice.
  onError: (message: string) => void;
  onClearError: () => void;
}

/**
 * Everything an operator needs to get the printer straight without touching a number:
 * align the media, then report which way the text drifts. Deliberately carries no
 * permission gate — the nudge endpoint runs on the same access as printing, because it
 * accepts an observation rather than a value.
 */
function LotLabelPrinterControls({
  isBusy,
  nudgeCalibration,
  onTestPrint,
  onFeed,
  onError,
  onClearError,
}: LotLabelPrinterControlsProps) {
  // Outcome of the last adjustment, cleared as soon as another one is attempted.
  // "at-limit" means the server refused to move any further in that direction.
  const [nudgeOutcome, setNudgeOutcome] = useState<"adjusted" | "at-limit" | null>(
    null,
  );

  // Reports the observed drift; the server turns it into a pitch correction and saves it.
  const handleNudge = (
    direction: LabelDriftDirection,
    speed: LabelDriftSpeed,
  ) => {
    onClearError();
    setNudgeOutcome(null);
    nudgeCalibration.mutate(
      { direction, speed },
      {
        onSuccess: (response) =>
          setNudgeOutcome(response?.isAtLimit ? "at-limit" : "adjusted"),
        onError: (err) =>
          onError(`Chyba při úpravě kalibrace: ${(err as Error).message}`),
      },
    );
  };

  return (
    <div className="mb-6 pt-4 border-t border-gray-200 dark:border-graphite-border">
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={onTestPrint}
          disabled={isBusy}
          title="Vytiskne kříž pro zarovnání média"
          className="px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
        >
          Zkušební kříž
        </button>
        {FEED_STEPS.map((steps) => (
          <button
            key={steps}
            type="button"
            onClick={() => onFeed(FEED_STEP_DOTS * steps)}
            disabled={isBusy}
            title={`Posunout médium o ${steps} vpřed`}
            className="flex items-center gap-1 px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
          >
            <ChevronDown className="h-4 w-4" />+{steps}
          </button>
        ))}
      </div>

      <div className="mt-4">
        <p className="text-sm font-medium text-gray-700 dark:text-graphite-muted">
          Text na štítcích se posouvá:
        </p>
        <p className="text-xs text-gray-500 dark:text-graphite-muted mt-1">
          Vytiskněte dávku štítků a sledujte, kam text na štítku putuje.
        </p>

        <div className="mt-3 grid grid-cols-2 gap-2">
          {DRIFT_OPTIONS.map(({ direction, speed, label, hint, Icon }) => (
            <button
              key={`${direction}-${speed}`}
              type="button"
              onClick={() => handleNudge(direction, speed)}
              disabled={isBusy}
              // Keeps the accessible name to the observation itself; the label-count
              // hint stays visible but is not read out as part of the button's name.
              aria-label={label}
              className="flex items-center gap-2 px-3 py-2 text-left bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
            >
              <Icon className="h-5 w-5 shrink-0 text-indigo-600 dark:text-graphite-accent" />
              <span>
                <span className="block text-sm font-medium text-gray-700 dark:text-graphite-text">
                  {label}
                </span>
                <span className="block text-xs text-gray-500 dark:text-graphite-muted">
                  {hint}
                </span>
              </span>
            </button>
          ))}
        </div>

        {nudgeOutcome === "adjusted" && (
          <div
            role="status"
            className="mt-3 flex items-start gap-2 bg-green-50 dark:bg-green-900/30 border border-green-200 dark:border-green-900/40 text-green-800 dark:text-green-300 px-3 py-2 rounded text-sm"
          >
            <CheckCircle2 className="h-4 w-4 mt-0.5 shrink-0" />
            <span>
              Nastavení upraveno. Vytiskněte další dávku a zkontrolujte, jestli
              se text pořád posouvá.
            </span>
          </div>
        )}

        {nudgeOutcome === "at-limit" && (
          <div
            role="status"
            className="mt-3 flex items-start gap-2 bg-amber-50 dark:bg-amber-900/30 border border-amber-200 dark:border-amber-900/40 text-amber-800 dark:text-amber-300 px-3 py-2 rounded text-sm"
          >
            <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
            <span>
              Nastavení už je na kraji rozsahu a dál se tímto směrem posunout
              nedá. Zkontrolujte, jestli je založené správné médium.
            </span>
          </div>
        )}
      </div>
    </div>
  );
}

export default LotLabelPrinterControls;
