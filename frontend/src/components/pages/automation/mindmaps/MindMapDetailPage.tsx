import React, { useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { ArrowLeft, RefreshCw, Save } from "lucide-react";
import toast from "react-hot-toast";
import UnsavedChangesDialog from "../../../dialogs/UnsavedChangesDialog";
import { useUnsavedChangesDialog } from "../../../../hooks/useUnsavedChangesDialog";
import {
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
    try {
      const result = await saveDocument.mutateAsync({
        mindMapId: id,
        documentJson: JSON.stringify(localDoc),
      });
      // The server response carries real node ids (in place of our tmp- ones)
      // and the locks it just applied — always trust it over the local copy.
      loadedJsonRef.current = result.documentJson;
      setLocalDoc(parseDocument(result.documentJson));
      setIsDirty(false);
      toast.success("Mapa uložena");
      return true;
    } catch {
      toast.error("Uložení mapy se nezdařilo");
      return false;
    }
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
  if (error) {
    return <div className="p-8 text-gray-500 dark:text-graphite-muted">Nepodařilo se načíst mapu</div>;
  }
  if (!detail) {
    return <div className="p-8 text-gray-500 dark:text-graphite-muted">Mapa nenalezena</div>;
  }

  const badge = STATUS_BADGE[detail.status] ?? { label: detail.status, ...DEFAULT_STATUS_BADGE };
  const hasPendingMeeting = detail.meetings.some((m) => !m.processedAt);
  const canRegenerate = detail.status === "Failed" || hasPendingMeeting;

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
