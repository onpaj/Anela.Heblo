import React, { useEffect, useRef, useState } from "react";
import { Save, Trash2, FolderOpen } from "lucide-react";
import {
  usePricingScenariosQuery,
  usePricingScenarioQuery,
  useSavePricingScenarioMutation,
  useDeletePricingScenarioMutation,
} from "../../api/hooks/usePricingSimulator";
import {
  IPricingOverrideDto,
  PricingRowDto,
  PricingScenarioSummaryDto,
  PricingTotalsDto,
  ProductType,
} from "../../api/generated/api-client";
import { resolveSwaggerErrorMessage } from "../../utils/errorHandler";
import { usePermissionsContext } from "../../auth/PermissionsContext";

export interface PricingScenarioBarProps {
  productCode?: string;
  productName?: string;
  productType?: ProductType;
  // The currently effective overrides -- what gets saved when the user clicks Uložit.
  overrides: IPricingOverrideDto[];
  // Settles any recalculation still in flight and returns the overrides as of AFTER it
  // landed. Clicking Uložit blurs the cell being edited, which STARTS a recalculation --
  // so the `overrides` prop captured at click time is a render behind and would persist
  // the scenario without the edit the user just made. When omitted, `overrides` is used.
  resolveOverrides?: () => Promise<IPricingOverrideDto[]>;
  // Fired once a scenario's detail has loaded, with everything needed to replace the
  // grid's rows/totals/overrides wholesale (the server's overrides array is
  // authoritative -- the caller must never hand-merge it).
  onScenarioLoaded: (
    scenario: PricingScenarioSummaryDto,
    rows: PricingRowDto[],
    totals: PricingTotalsDto | undefined,
    overrides: IPricingOverrideDto[],
  ) => void;
  // Fired after a successful save, with the name that was saved.
  onScenarioSaved?: (name: string) => void;
}

const GENERIC_SAVE_FAILURE = "Scénář se nepodařilo uložit, zkuste to prosím znovu.";
const GENERIC_DELETE_FAILURE = "Scénář se nepodařilo smazat, zkuste to prosím znovu.";
const EMPTY_NAME_MESSAGE = "Zadejte název scénáře.";
const READ_ONLY_TITLE = "Nemáte oprávnění k úpravě scénářů.";

const PricingScenarioBar: React.FC<PricingScenarioBarProps> = ({
  productCode,
  productName,
  productType,
  overrides,
  resolveOverrides,
  onScenarioLoaded,
  onScenarioSaved,
}) => {
  const { hasPermission, isLoading: permissionsLoading } = usePermissionsContext();
  const canWrite = !permissionsLoading && hasPermission("finance.price_analysis.write");

  const [selectedId, setSelectedId] = useState("");
  const [nameInput, setNameInput] = useState("");
  const [saveError, setSaveError] = useState<string | undefined>(undefined);
  const [deleteError, setDeleteError] = useState<string | undefined>(undefined);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  const scenariosQuery = usePricingScenariosQuery();
  const scenarioDetailQuery = usePricingScenarioQuery(selectedId || undefined);
  const saveMutation = useSavePricingScenarioMutation();
  const deleteMutation = useDeletePricingScenarioMutation();

  const scenarios = scenariosQuery.data?.scenarios ?? [];

  // Loading a scenario replaces rows/totals/overrides wholesale in the parent (see
  // PriceAnalysis.handleScenarioLoaded) -- this effect only fires the callback once
  // per newly-selected scenario's data landing, it never merges anything itself.
  //
  // Guarded by a ref rather than by the dep array alone: `data` is a NEW object on every
  // refetch, and a refetch happens without the user selecting anything -- react-query's
  // default focus refetch once `staleTime` (5 min, App.tsx) has passed, and our own
  // save/delete invalidation, whose `["pricing-scenarios"]` key prefix-matches the
  // `["pricing-scenarios", id]` detail query. Re-firing there replayed the SAVED snapshot
  // over the grid and silently discarded every edit made since the load.
  const appliedScenarioIdRef = useRef<string | null>(null);
  useEffect(() => {
    if (!selectedId || !scenarioDetailQuery.data?.scenario) {
      return;
    }
    if (appliedScenarioIdRef.current === selectedId) {
      return;
    }
    appliedScenarioIdRef.current = selectedId;
    const { scenario, rows, totals, overrides: loadedOverrides } = scenarioDetailQuery.data;
    setNameInput(scenario.name ?? "");
    onScenarioLoaded(scenario, rows ?? [], totals, loadedOverrides ?? []);
    // onScenarioLoaded is expected to be referentially stable enough for this --
    // re-running only when the selected scenario or its fetched data changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, scenarioDetailQuery.data]);

  const handleSelectScenario = (id: string) => {
    // An explicit pick from the dropdown is the one gesture that MAY replace the grid,
    // so clear the guard and let the effect apply this scenario's data when it lands.
    appliedScenarioIdRef.current = null;
    setSelectedId(id);
    setShowDeleteConfirm(false);
    setSaveError(undefined);
    setDeleteError(undefined);
  };

  const handleSave = async () => {
    const trimmedName = nameInput.trim();
    if (!trimmedName) {
      // Blocked client-side -- no request fired for an empty/whitespace-only name. Say so:
      // returning silently left the user clicking a live button with nothing happening.
      setSaveError(EMPTY_NAME_MESSAGE);
      return;
    }
    setSaveError(undefined);
    try {
      // Settle any recalculation still in flight first, so the edit that this very click
      // committed (blurring the cell starts the request) is part of what gets persisted.
      const effectiveOverrides = resolveOverrides ? await resolveOverrides() : overrides;
      const response = await saveMutation.mutateAsync({
        id: selectedId || undefined,
        name: trimmedName,
        productCode,
        productName,
        productType,
        overrides: effectiveOverrides,
      });
      if (response.id) {
        // Saving does not re-load the grid -- mark this id applied so the invalidation
        // that follows cannot replay the server snapshot over the user's live edits.
        appliedScenarioIdRef.current = response.id;
        setSelectedId(response.id);
      }
      onScenarioSaved?.(trimmedName);
    } catch (error) {
      setSaveError(resolveSwaggerErrorMessage(error) ?? GENERIC_SAVE_FAILURE);
    }
  };

  const handleConfirmDelete = async () => {
    if (!selectedId) {
      return;
    }
    setDeleteError(undefined);
    try {
      await deleteMutation.mutateAsync(selectedId);
      setSelectedId("");
      setNameInput("");
    } catch (error) {
      setDeleteError(resolveSwaggerErrorMessage(error) ?? GENERIC_DELETE_FAILURE);
    } finally {
      setShowDeleteConfirm(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 flex-wrap">
        <FolderOpen className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
        <select
          data-testid="pricing-scenario-select"
          value={selectedId}
          onChange={(e) => handleSelectScenario(e.target.value)}
          className="focus:ring-indigo-500 focus:border-indigo-500 py-2 px-3 text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md"
        >
          <option value="">— Vyberte scénář —</option>
          {scenarios.map((scenario) => (
            <option key={scenario.id} value={scenario.id}>
              {scenario.name}
              {scenario.editedProductCount != null ? ` (${scenario.editedProductCount})` : ""}
            </option>
          ))}
        </select>

        <input
          type="text"
          data-testid="pricing-scenario-name-input"
          value={nameInput}
          onChange={(e) => setNameInput(e.target.value)}
          placeholder="Název scénáře"
          className="focus:ring-indigo-500 focus:border-indigo-500 py-2 px-3 text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
        />

        <button
          type="button"
          data-testid="pricing-scenario-save"
          onClick={handleSave}
          disabled={saveMutation.isPending || !canWrite}
          title={canWrite ? undefined : READ_ONLY_TITLE}
          className="inline-flex items-center gap-1 bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
        >
          <Save className="h-4 w-4" />
          Uložit
        </button>

        {!showDeleteConfirm ? (
          <button
            type="button"
            data-testid="pricing-scenario-delete"
            onClick={() => setShowDeleteConfirm(true)}
            disabled={!selectedId || !canWrite}
            title={canWrite ? undefined : READ_ONLY_TITLE}
            className="inline-flex items-center gap-1 text-sm font-medium text-red-600 hover:text-red-700 disabled:text-gray-400 disabled:cursor-not-allowed dark:text-red-400 dark:hover:text-red-300 dark:disabled:text-graphite-faint"
          >
            <Trash2 className="h-4 w-4" />
            Smazat
          </button>
        ) : (
          // Inline confirmation -- deliberately NOT window.confirm, which blocks the
          // Playwright E2E in Task 12 and hangs browser automation.
          <div
            data-testid="pricing-scenario-delete-confirm"
            className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text"
          >
            <span>Opravdu smazat scénář „{nameInput}“?</span>
            <button
              type="button"
              data-testid="pricing-scenario-delete-confirm-yes"
              onClick={handleConfirmDelete}
              disabled={deleteMutation.isPending}
              className="font-medium text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
            >
              Ano, smazat
            </button>
            <button
              type="button"
              data-testid="pricing-scenario-delete-confirm-no"
              onClick={() => setShowDeleteConfirm(false)}
              className="font-medium text-gray-500 hover:text-gray-700 dark:text-graphite-muted dark:hover:text-graphite-text"
            >
              Zrušit
            </button>
          </div>
        )}
      </div>

      {saveError && (
        <div
          data-testid="pricing-scenario-save-error"
          className="text-xs font-medium text-red-600 dark:text-red-400"
        >
          {saveError}
        </div>
      )}
      {deleteError && (
        <div
          data-testid="pricing-scenario-delete-error"
          className="text-xs font-medium text-red-600 dark:text-red-400"
        >
          {deleteError}
        </div>
      )}
    </div>
  );
};

export default PricingScenarioBar;
