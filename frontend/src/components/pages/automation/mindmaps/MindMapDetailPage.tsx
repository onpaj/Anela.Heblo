import React, { useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, RefreshCw, Save } from "lucide-react";
import toast from "react-hot-toast";
import UnsavedChangesDialog from "../../../dialogs/UnsavedChangesDialog";
import { useUnsavedChangesDialog } from "../../../../hooks/useUnsavedChangesDialog";
import {
  MIND_MAPS_KEYS,
  MindMapDetail,
  useMindMapDetail,
  useRegenerateMindMap,
  useSaveMindMapDocument,
} from "../../../../api/hooks/useMindMaps";
import {
  addChildNode,
  deleteNode,
  MindMapDocument,
  MindMapNode,
  parseDocument,
  setNodePosition,
  toggleCollapsed,
  updateNodeFields,
} from "./mindMapDocument";
import MindMapCanvas from "./MindMapCanvas";
import MindMapSidePanel from "./MindMapSidePanel";
import { PAGE_CONTAINER_HEIGHT } from "../../../../constants/layout";
import { useScreenView } from "../../../../telemetry/useScreenView";

const STATUS_BADGE: Record<string, { label: string; className: string }> = {
  Idle: { label: "Aktuální", className: "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-300" },
  Updating: { label: "Aktualizuje se…", className: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300" },
  Failed: { label: "Chyba", className: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300" },
};
const DEFAULT_STATUS_BADGE = { className: "bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted" };

const MindMapDetailPage: React.FC = () => {
  useScreenView("Automation", "MindMapDetail");
  const { id } = useParams<{ id: string }>();
  const queryClient = useQueryClient();
  const { data: detail, isLoading, error } = useMindMapDetail(id ?? "");
  const saveDocument = useSaveMindMapDocument();
  const regenerate = useRegenerateMindMap();

  const [localDoc, setLocalDoc] = useState<MindMapDocument | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const loadedJsonRef = useRef<string | null>(null);
  const titleInputRef = useRef<HTMLInputElement>(null);

  const isReadOnly = detail?.status === "Updating";

  // Adopt server document whenever it changes and there are no local edits. This
  // is what keeps the 3s poll during a background "Updating" run from silently
  // discarding whatever the user is currently typing: as long as isDirty is true
  // the local copy is left alone, no matter how many times detail refetches.
  useEffect(() => {
    if (!detail) return;
    if (!isDirty && detail.documentJson !== loadedJsonRef.current) {
      loadedJsonRef.current = detail.documentJson;
      setLocalDoc(parseDocument(detail.documentJson));
    }
  }, [detail, isDirty]);

  const applyEdit = (next: MindMapDocument) => {
    setLocalDoc(next);
    setIsDirty(true);
  };

  const handleSave = async (): Promise<boolean> => {
    if (!id || !localDoc) return false;

    let result: { documentJson: string };
    try {
      result = await saveDocument.mutateAsync({
        mindMapId: id,
        documentJson: JSON.stringify(localDoc),
      });
    } catch {
      toast.error("Uložení mapy se nezdařilo");
      return false;
    }

    // From here the save has already succeeded server-side. useSaveMindMapDocument's
    // onSuccess fires invalidateQueries, but that only *starts* a background refetch —
    // it does not wait for it. Without writing the canonical result into the query
    // cache ourselves, `detail` (read from that same cache) would still hold the
    // pre-save document for the whole refetch window; the adoption effect below would
    // then see `!isDirty` flip true against that stale `detail.documentJson` and
    // immediately revert both `localDoc` and `loadedJsonRef` back to the pre-save
    // state — visibly undoing a save that actually succeeded (tmp- ids and missing
    // locks reappear), and re-armed for a duplicate-node bug on the next save if the
    // user keeps editing during that window.
    queryClient.setQueryData<MindMapDetail>(MIND_MAPS_KEYS.detail(id), (old) =>
      old ? { ...old, documentJson: result.documentJson } : old,
    );
    loadedJsonRef.current = result.documentJson;
    setIsDirty(false);

    // A malformed canonical response is a distinct failure from a failed save
    // request — the mutation already succeeded, so this must not be reported as
    // "Uložení mapy se nezdařilo" (that would be actively misleading).
    try {
      setLocalDoc(parseDocument(result.documentJson));
      toast.success("Mapa uložena");
    } catch {
      toast.error("Mapa byla uložena, ale odpověď serveru se nepodařilo zobrazit. Načtěte stránku znovu.");
    }
    return true;
  };

  const { dialogProps, requestNavigation } = useUnsavedChangesDialog(isDirty, handleSave);

  const handleUpdateNode = (
    nodeId: string,
    patch: Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>,
  ) => {
    if (!localDoc) return;
    applyEdit(updateNodeFields(localDoc, nodeId, patch));
  };

  const focusTitleInput = () => {
    requestAnimationFrame(() => titleInputRef.current?.focus());
  };

  const handleAddChild = (parentId: string) => {
    if (!localDoc) return;
    const { doc, newNodeId } = addChildNode(localDoc, parentId, "Nový uzel");
    applyEdit(doc);
    setSelectedNodeId(newNodeId);
    focusTitleInput();
  };

  const handleDeleteNode = (nodeId: string) => {
    if (!localDoc) return;
    applyEdit(deleteNode(localDoc, nodeId));
    if (selectedNodeId === nodeId) setSelectedNodeId(null);
  };

  const handleToggleCollapsed = (nodeId: string) => {
    if (!localDoc) return;
    applyEdit(toggleCollapsed(localDoc, nodeId));
  };

  const handleNodeDragStop = (nodeId: string, position: { x: number; y: number }) => {
    if (!localDoc) return;
    applyEdit(setNodePosition(localDoc, nodeId, position));
  };

  const handleNodeDoubleClick = (nodeId: string) => {
    setSelectedNodeId(nodeId);
    if (!isReadOnly) focusTitleInput();
  };

  const handleRegenerate = async () => {
    if (!id) return;
    // Regenerating replaces the document with Claude's rewrite. Doing that while
    // the user has unsaved local edits would either bury the rewrite under a
    // subsequent save of the stale local copy, or (per the adoption guard) get
    // silently ignored until the user saves anyway — either way real work is lost
    // silently. Force a save first instead.
    if (isDirty) {
      toast.error("Nejprve uložte mapu, poté ji můžete regenerovat.");
      return;
    }
    try {
      await regenerate.mutateAsync(id);
      toast.success("Regenerace mapy byla spuštěna");
    } catch {
      toast.error("Spuštění regenerace se nezdařilo");
    }
  };

  if (isLoading) {
    return <div className="p-8 text-gray-500 dark:text-graphite-muted">Načítání...</div>;
  }
  // React Query retains the last good `data` across a failed refetch (e.g. one
  // transient poll failure while status is "Updating"). Only treat this as a hard
  // error — and drop the whole editor, including any unsaved edits' visual state —
  // when there is truly no data to fall back on.
  if (error && !detail) {
    return <div className="p-8 text-gray-500 dark:text-graphite-muted">Nepodařilo se načíst mapu</div>;
  }
  if (!detail) {
    return <div className="p-8 text-gray-500 dark:text-graphite-muted">Mapa nenalezena</div>;
  }

  const badge = STATUS_BADGE[detail.status] ?? { label: detail.status, ...DEFAULT_STATUS_BADGE };
  const hasPendingMeeting = detail.meetings.some((m) => !m.processedAt);
  const canRegenerate = detail.status === "Failed" || hasPendingMeeting;
  // The adoption effect intentionally refuses to overwrite unsaved edits — but that
  // means a newer server document (e.g. from a just-finished regeneration) can sit
  // unapplied for as long as the user keeps editing. Surface that rather than
  // silently doing nothing.
  const hasNewerServerVersion = isDirty && detail.documentJson !== loadedJsonRef.current;

  return (
    <div className="flex flex-col w-full overflow-hidden" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="px-4 sm:px-6 lg:px-8 py-3 shrink-0">
        <button
          type="button"
          onClick={() => requestNavigation("/automation/mind-maps")}
          className="inline-flex items-center text-sm text-indigo-700 dark:text-graphite-accent hover:underline"
        >
          <ArrowLeft className="w-4 h-4 mr-1" /> Zpět na seznam
        </button>
      </div>

      <div className="px-4 sm:px-6 lg:px-8 flex items-start justify-between gap-4 shrink-0">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-graphite-text">{detail.name}</h1>
          {detail.description && (
            <p className="mt-1 text-sm text-gray-600 dark:text-graphite-muted">{detail.description}</p>
          )}
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <span
            data-testid="mindmap-status-badge"
            title={detail.status === "Failed" ? detail.lastError ?? undefined : undefined}
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ${badge.className}`}
          >
            {detail.status === "Updating" && <RefreshCw className="w-3 h-3 animate-spin" />}
            {badge.label}
          </span>
          {canRegenerate && (
            <button
              type="button"
              data-testid="mindmap-regenerate-button"
              onClick={handleRegenerate}
              disabled={regenerate.isPending}
              className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 dark:border-graphite-border hover:bg-gray-50 dark:hover:bg-white/5 dark:text-graphite-muted disabled:opacity-50"
            >
              <RefreshCw className={`w-4 h-4 mr-1 ${regenerate.isPending ? "animate-spin" : ""}`} />
              Regenerovat
            </button>
          )}
          <button
            type="button"
            data-testid="mindmap-save-button"
            onClick={handleSave}
            disabled={!isDirty || isReadOnly || saveDocument.isPending}
            className="inline-flex items-center px-3 py-1.5 text-sm rounded-lg font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <Save className="w-4 h-4 mr-1" />
            {saveDocument.isPending ? "Ukládám..." : "Uložit"}
          </button>
        </div>
      </div>

      {isReadOnly && (
        <div className="px-4 sm:px-6 lg:px-8 mt-3 shrink-0">
          <div className="rounded-md border border-amber-200 bg-amber-50 dark:border-amber-900/40 dark:bg-amber-900/20 px-3 py-2 text-sm text-amber-800 dark:text-amber-300">
            Mapa se právě aktualizuje — úpravy jsou dočasně zamčené.
          </div>
        </div>
      )}

      {hasNewerServerVersion && (
        <div className="px-4 sm:px-6 lg:px-8 mt-3 shrink-0">
          <div className="rounded-md border border-sky-200 bg-sky-50 dark:border-sky-900/40 dark:bg-sky-900/20 px-3 py-2 text-sm text-sky-800 dark:text-sky-300">
            Na serveru je k dispozici novější verze mapy (např. z dokončené regenerace). Uložte nebo zahoďte své
            úpravy, aby se načetla.
          </div>
        </div>
      )}

      <div className="flex-1 flex overflow-hidden mt-3 px-4 sm:px-6 lg:px-8 pb-4 gap-3">
        <div className="flex-1 min-h-[70vh] border border-gray-200 dark:border-graphite-border rounded-lg overflow-hidden">
          {localDoc && (
            <MindMapCanvas
              document={localDoc}
              isReadOnly={isReadOnly}
              selectedNodeId={selectedNodeId}
              onSelectNode={setSelectedNodeId}
              onNodeDragStop={handleNodeDragStop}
              onNodeDoubleClick={handleNodeDoubleClick}
            />
          )}
        </div>
        {localDoc && (
          <MindMapSidePanel
            detail={detail}
            document={localDoc}
            selectedNodeId={selectedNodeId}
            isReadOnly={isReadOnly}
            isDirty={isDirty}
            onUpdateNode={handleUpdateNode}
            onAddChild={handleAddChild}
            onDeleteNode={handleDeleteNode}
            onToggleCollapsed={handleToggleCollapsed}
            titleInputRef={titleInputRef}
          />
        )}
      </div>

      <UnsavedChangesDialog {...dialogProps} />
    </div>
  );
};

export default MindMapDetailPage;
