import React, { useCallback, useEffect, useRef, useState } from "react";
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
import { MindMapDocument, parseDocument } from "./mindMapDocument";
import MindMapCanvas, { MindMapCanvasHandle, MindMapNodePatch } from "./MindMapCanvas";
import MindMapSidePanel from "./MindMapSidePanel";
import MindMapToolbar from "./MindMapToolbar";
import MindMapHelpSheet from "./MindMapHelpSheet";
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

  const canvasRef = useRef<MindMapCanvasHandle>(null);
  const [loadedJson, setLoadedJson] = useState<string | null>(null);
  const [loadedDoc, setLoadedDoc] = useState<MindMapDocument | null>(null);
  const [panelDoc, setPanelDoc] = useState<MindMapDocument | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [isHelpOpen, setIsHelpOpen] = useState(false);
  const [hasDocumentParseError, setHasDocumentParseError] = useState(false);

  const isReadOnly = detail?.status === "Updating";

  // Adopt a server document only when there is nothing unsaved to lose. `loadedJson`
  // doubles as the canvas's revision token: bumping it is what reloads the map.
  useEffect(() => {
    if (!detail) return;
    if (isDirty || detail.documentJson === loadedJson) return;
    try {
      const parsed = parseDocument(detail.documentJson);
      setLoadedDoc(parsed);
      setPanelDoc(parsed);
      setLoadedJson(detail.documentJson);
      setHasDocumentParseError(false);
    } catch {
      setHasDocumentParseError(true);
    }
  }, [detail, isDirty, loadedJson]);

  // Any edit inside the canvas. Pulling a fresh snapshot here is what keeps the
  // side panel showing the node's real current values.
  const handleCanvasChange = useCallback(() => {
    setIsDirty(true);
    const snapshot = canvasRef.current?.getDocument();
    if (snapshot) setPanelDoc(snapshot);
  }, []);

  const handleSelectNode = useCallback((nodeId: string | null) => {
    setSelectedNodeId(nodeId);
    const snapshot = canvasRef.current?.getDocument();
    if (snapshot) setPanelDoc(snapshot);
  }, []);

  const handleUpdateNode = useCallback(
    (nodeId: string, patch: MindMapNodePatch) => {
      if (isReadOnly) return;
      canvasRef.current?.patchNode(nodeId, patch);
    },
    [isReadOnly],
  );

  const handleSave = useCallback(async (): Promise<boolean> => {
    const documentToSave = canvasRef.current?.getDocument();
    if (!id || !documentToSave) return false;

    let result: { documentJson: string };
    try {
      result = await saveDocument.mutateAsync({
        mindMapId: id,
        documentJson: JSON.stringify(documentToSave),
      });
    } catch {
      toast.error("Uložení mapy se nezdařilo");
      return false;
    }

    // The save succeeded server-side. Write the canonical result into the query
    // cache and adopt it as the loaded revision in the same pass: if `loadedJson`
    // stayed behind, the adoption effect would see a "newer" document the moment
    // isDirty flips false and reload the map out from under the user — visibly
    // undoing a save that actually worked.
    queryClient.setQueryData<MindMapDetail>(MIND_MAPS_KEYS.detail(id), (old) =>
      old ? { ...old, documentJson: result.documentJson } : old,
    );
    setIsDirty(false);

    try {
      const parsed = parseDocument(result.documentJson);
      setLoadedDoc(parsed);
      setPanelDoc(parsed);
      setLoadedJson(result.documentJson);
      toast.success("Mapa uložena");
    } catch {
      toast.error("Mapa byla uložena, ale odpověď serveru se nepodařilo zobrazit. Načtěte stránku znovu.");
    }
    return true;
  }, [id, saveDocument, queryClient]);

  const { dialogProps, requestNavigation } = useUnsavedChangesDialog(isDirty, handleSave);

  // ⌘S is the one shortcut that stays document-wide: it must work while the user is
  // typing in the side panel, not only when the canvas has focus.
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.metaKey || event.ctrlKey) || event.key.toLowerCase() !== "s") return;
      event.preventDefault();
      if (isDirty && !isReadOnly) void handleSave();
    };
    window.document.addEventListener("keydown", onKeyDown);
    return () => window.document.removeEventListener("keydown", onKeyDown);
  }, [handleSave, isDirty, isReadOnly]);

  // Map names are user-supplied Czech text and may contain characters that are
  // unsafe or awkward in a filename (path separators, control characters, a name
  // that is empty after trimming). Replace anything outside a conservative safe
  // set rather than trusting the raw name verbatim.
  const sanitizeFileName = (name: string): string => {
    const sanitized = name.trim().replace(/[\\/:*?"<>|]+/g, "_");
    return sanitized.length > 0 ? sanitized : "mapa";
  };

  const downloadBlob = (blob: Blob, extension: string) => {
    const url = URL.createObjectURL(blob);
    const link = window.document.createElement("a");
    link.href = url;
    link.download = `${sanitizeFileName(detail?.name ?? "mapa")}.${extension}`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const handleExportPng = async () => {
    const blob = await canvasRef.current?.exportPng();
    if (!blob) {
      toast.error("Export mapy do PNG se nezdařil");
      return;
    }
    downloadBlob(blob, "png");
  };

  const handleExportSvg = () => {
    const blob = canvasRef.current?.exportSvg();
    if (!blob) {
      toast.error("Export mapy do SVG se nezdařil");
      return;
    }
    downloadBlob(blob, "svg");
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
  if (hasDocumentParseError && !loadedDoc) {
    return (
      <div className="p-8 text-gray-500 dark:text-graphite-muted">
        Dokument mapy se nepodařilo načíst — data ze serveru jsou poškozená.
      </div>
    );
  }

  const badge = STATUS_BADGE[detail.status] ?? { label: detail.status, ...DEFAULT_STATUS_BADGE };
  const hasPendingMeeting = detail.meetings.some((m) => !m.processedAt);
  const canRegenerate = detail.status === "Failed" || hasPendingMeeting;
  // The adoption effect intentionally refuses to overwrite unsaved edits — but that
  // means a newer server document (e.g. from a just-finished regeneration) can sit
  // unapplied for as long as the user keeps editing. Surface that rather than
  // silently doing nothing.
  const hasNewerServerVersion = isDirty && detail.documentJson !== loadedJson;

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
            title="⌘S"
            // Amber while there are unsaved changes, matching the template's "the save
            // button stays orange until it is safe to close" cue.
            className={`inline-flex items-center px-3 py-1.5 text-sm rounded-lg font-medium text-white disabled:opacity-50 disabled:cursor-not-allowed ${
              isDirty ? "bg-amber-600 hover:bg-amber-700" : "bg-indigo-600 hover:bg-indigo-700"
            }`}
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

      {hasDocumentParseError && loadedDoc && (
        <div className="px-4 sm:px-6 lg:px-8 mt-3 shrink-0">
          <div className="rounded-md border border-red-200 bg-red-50 dark:border-red-900/40 dark:bg-red-900/20 px-3 py-2 text-sm text-red-800 dark:text-red-300">
            Poslední verzi mapy ze serveru se nepodařilo načíst — zobrazuje se předchozí stav.
          </div>
        </div>
      )}

      <div className="flex-1 flex overflow-hidden mt-3 px-4 sm:px-6 lg:px-8 pb-4 gap-3">
        <div className="relative flex-1 min-h-[70vh] border border-gray-200 dark:border-graphite-border rounded-lg overflow-hidden bg-[#FAF8F5] dark:bg-graphite-bg">
          {loadedDoc && (
            <>
              <MindMapToolbar
                isReadOnly={isReadOnly}
                hasSelection={selectedNodeId !== null}
                onExpandAll={() => canvasRef.current?.expandAll()}
                onCollapseAll={() => canvasRef.current?.collapseAll()}
                onFit={() => canvasRef.current?.fit()}
                onAddSibling={() => canvasRef.current?.addSibling()}
                onAddChild={() => canvasRef.current?.addChild()}
                onUndo={() => canvasRef.current?.undo()}
                onOpenHelp={() => setIsHelpOpen(true)}
                onExportPng={handleExportPng}
                onExportSvg={handleExportSvg}
              />
              <MindMapCanvas
                ref={canvasRef}
                initialDocument={loadedDoc}
                documentRevision={loadedJson ?? ""}
                isReadOnly={isReadOnly}
                onChange={handleCanvasChange}
                onSelectNode={handleSelectNode}
              />
            </>
          )}
        </div>
        {loadedDoc && panelDoc && (
          <MindMapSidePanel
            detail={detail}
            document={panelDoc}
            selectedNodeId={selectedNodeId}
            isReadOnly={isReadOnly}
            isDirty={isDirty}
            onUpdateNode={handleUpdateNode}
          />
        )}
      </div>

      {isHelpOpen && <MindMapHelpSheet onClose={() => setIsHelpOpen(false)} />}

      <UnsavedChangesDialog {...dialogProps} />
    </div>
  );
};

export default MindMapDetailPage;
