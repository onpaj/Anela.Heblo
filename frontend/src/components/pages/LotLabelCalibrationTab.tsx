import { useEffect, useState } from "react";
import {
  useLotLabelCalibration,
  useSetLotLabelCalibration,
} from "../../api/hooks/useMaterialContainers";
import { usePermissionsContext } from "../../auth/PermissionsContext";

// Editing the printer pitch/drift calibration has its own permission: it affects every
// printed label, so it is not granted by plain material-containers write access. Both
// levels are needed to use the form — the API requires Read to load the current values
// and Write to save them, so holding only one leaves an unusable form. The drift buttons
// on the print tab need neither: they cannot set a value, only report an observation.
export const CALIBRATION_READ_PERMISSION = "manufacture.label_calibration.read";
export const CALIBRATION_WRITE_PERMISSION = "manufacture.label_calibration.write";

/** True when the current user may both read and save the raw calibration values. */
export const useCanCalibrate = (): boolean => {
  const { hasPermission } = usePermissionsContext();
  return (
    hasPermission(CALIBRATION_READ_PERMISSION) &&
    hasPermission(CALIBRATION_WRITE_PERMISSION)
  );
};

interface LotLabelCalibrationTabProps {
  // Reported into the modal's shared error banner rather than rendered twice.
  onError: (message: string) => void;
  onClearError: () => void;
}

/**
 * The raw pitch/drift values, for someone who knows what a dot is. The tab itself is open
 * to everyone, as it always has been; without the calibration permission it explains
 * itself instead of rendering the fields. Operators adjust the same numbers indirectly
 * through the drift buttons on the print tab.
 */
function LotLabelCalibrationTab({
  onError,
  onClearError,
}: LotLabelCalibrationTabProps) {
  const [pitchDots, setPitchDots] = useState<number | "">("");
  const [driftPer100, setDriftPer100] = useState<number | "">("");
  // True once the user edits either calibration field, until the values are saved or the
  // tab is left. Blocks a background refetch from overwriting unsaved input.
  const [isCalibrationDirty, setIsCalibrationDirty] = useState(false);

  // Gates the fields only, never the tab. The query is disabled without it because the
  // API requires the read permission and would reject the call.
  const canCalibrate = useCanCalibrate();

  // This component only renders while the modal is open on the calibration tab, so
  // mounting is itself the "the operator is looking at it now" signal; the query's
  // refetchOnMount keeps the fields from showing a stale pitch.
  const calibration = useLotLabelCalibration(canCalibrate);
  const saveCalibration = useSetLotLabelCalibration();

  // Populate the calibration fields once the persisted values load. The query refetches
  // on window focus, so this must not run while the user has unsaved edits — otherwise
  // alt-tabbing away and back would silently replace what they typed.
  useEffect(() => {
    if (!isCalibrationDirty && calibration.data?.pitchDots != null) {
      setPitchDots(calibration.data.pitchDots);
    }
  }, [calibration.data?.pitchDots, isCalibrationDirty]);

  useEffect(() => {
    if (!isCalibrationDirty && calibration.data?.driftDotsPer100Labels != null) {
      setDriftPer100(calibration.data.driftDotsPer100Labels);
    }
  }, [calibration.data?.driftDotsPer100Labels, isCalibrationDirty]);

  // Persists the printer calibration (pitch + drift correction) so it applies to every
  // print. Requires the dedicated label-calibration permission, not plain operator access.
  const handleSaveCalibration = () => {
    if (pitchDots === "" || driftPer100 === "") return;
    onClearError();
    saveCalibration.mutate(
      { pitchDots, driftDotsPer100Labels: driftPer100 },
      {
        // Saved values are now the server's; let a refetch repopulate the fields again.
        onSuccess: () => setIsCalibrationDirty(false),
        onError: (err) =>
          onError(`Chyba při uložení kalibrace: ${(err as Error).message}`),
      },
    );
  };

  if (!canCalibrate) {
    return (
      <div className="mb-6 text-sm text-gray-500 dark:text-graphite-muted">
        <p>Na úpravu rozteče a korekce driftu nemáte oprávnění.</p>
        <p className="mt-2">
          Posun textu na štítcích můžete opravit tlačítky na záložce Tisk.
        </p>
      </div>
    );
  }

  return (
    <div className="mb-6 space-y-3">
      {calibration.isError && (
        <div
          className="text-sm text-red-600 dark:text-red-400"
          data-testid="calibration-load-error"
        >
          Kalibraci se nepodařilo načíst:{" "}
          {(calibration.error as Error)?.message}
        </div>
      )}
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
          onChange={(e) => {
            setIsCalibrationDirty(true);
            setPitchDots(e.target.value === "" ? "" : Number(e.target.value));
          }}
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
          onChange={(e) => {
            setIsCalibrationDirty(true);
            setDriftPer100(e.target.value === "" ? "" : Number(e.target.value));
          }}
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
  );
}

export default LotLabelCalibrationTab;
