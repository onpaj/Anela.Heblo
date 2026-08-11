import React, { useEffect, useState } from "react";
import toast from "react-hot-toast";
import { Plus, X } from "lucide-react";
import {
  AttachedMeeting,
  MindMapDetail,
  MindMapVersionInfo,
  useAttachMeeting,
  useDetachMeeting,
  useRestoreMindMapVersion,
} from "../../../../api/hooks/useMindMaps";
import { useMeetingTasksList } from "../../../../api/hooks/useMeetingTasks";
import { MindMapDocument, MindMapNode, MindMapNodeStatus } from "./mindMapDocument";

const STATUS_LABELS: Record<MindMapNodeStatus, string> = {
  active: "Aktivní",
  done: "Hotovo",
  blocked: "Blokováno",
  idea: "Nápad",
};
const STATUS_OPTIONS = Object.keys(STATUS_LABELS) as MindMapNodeStatus[];

const INPUT_CLASS =
  "w-full px-3 py-2 rounded-md text-sm border border-gray-300 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint disabled:opacity-60 disabled:cursor-not-allowed";

type SidePanelTab = "node" | "meetings" | "history";

const TAB_LABELS: Record<SidePanelTab, string> = {
  node: "Uzel",
  meetings: "Porady",
  history: "Historie",
};

export interface MindMapSidePanelProps {
  detail: MindMapDetail;
  document: MindMapDocument;
  selectedNodeId: string | null;
  isReadOnly: boolean;
  // Not part of the original brief's prop sketch, but required to implement the
  // "Historie" tab spec ("disabled when isReadOnly or dirty"): the page already
  // tracks isDirty and is the only place that can know it.
  isDirty: boolean;
  onUpdateNode: (
    nodeId: string,
    patch: Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>,
  ) => void;
}

// --- "Uzel" tab ---

interface CommitOnBlurFieldProps {
  id: string;
  label: string;
  value: string;
  disabled: boolean;
  rows?: number;
  testId?: string;
  onCommit: (value: string) => void;
}

/**
 * Text field that keeps its own draft and only reports on blur. Each commit reaches
 * mind-elixir's reshapeNode, which re-renders and re-lays-out the whole map — doing
 * that per keystroke makes typing visibly stutter.
 * `key`ing this component by node id (see NodeTab) is what resets the draft when
 * the user selects a different node.
 */
const CommitOnBlurField: React.FC<CommitOnBlurFieldProps> = ({
  id,
  label,
  value,
  disabled,
  rows,
  testId,
  onCommit,
}) => {
  const [draft, setDraft] = useState(value);
  const commit = () => {
    if (draft !== value) onCommit(draft);
  };
  const props = {
    id,
    value: draft,
    disabled,
    "data-testid": testId,
    className: INPUT_CLASS,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setDraft(e.target.value),
    onBlur: commit,
  };
  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
        {label}
      </label>
      {rows ? <textarea {...props} rows={rows} /> : <input {...props} type="text" />}
    </div>
  );
};

interface NodeTabProps {
  document: MindMapDocument;
  selectedNodeId: string | null;
  isReadOnly: boolean;
  onUpdateNode: MindMapSidePanelProps["onUpdateNode"];
}

const NodeTab: React.FC<NodeTabProps> = ({ document: doc, selectedNodeId, isReadOnly, onUpdateNode }) => {
  const node = doc.nodes.find((n) => n.id === selectedNodeId) ?? null;

  if (!node) {
    return <p className="text-sm text-gray-500 dark:text-graphite-muted">Vyberte uzel na plátně</p>;
  }

  return (
    <div className="space-y-4">
      <CommitOnBlurField
        key={`${node.id}-title`}
        id="mindmap-node-title"
        label="Název"
        testId="mindmap-panel-title-input"
        value={node.title}
        disabled={isReadOnly}
        onCommit={(title) => onUpdateNode(node.id, { title })}
      />

      <CommitOnBlurField
        key={`${node.id}-notes`}
        id="mindmap-node-notes"
        label="Poznámky"
        rows={4}
        value={node.notes ?? ""}
        disabled={isReadOnly}
        onCommit={(notes) => onUpdateNode(node.id, { notes: notes || null })}
      />

      <CommitOnBlurField
        key={`${node.id}-owner`}
        id="mindmap-node-owner"
        label="Vlastník"
        value={node.owner ?? ""}
        disabled={isReadOnly}
        onCommit={(owner) => onUpdateNode(node.id, { owner: owner || null })}
      />

      <div>
        <label htmlFor="mindmap-node-status" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
          Stav
        </label>
        <select
          id="mindmap-node-status"
          value={node.status}
          disabled={isReadOnly}
          onChange={(e) => onUpdateNode(node.id, { status: e.target.value as MindMapNodeStatus })}
          className={INPUT_CLASS}
        >
          {STATUS_OPTIONS.map((status) => (
            <option key={status} value={status}>
              {STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>

      {node.lockedBy && (
        <p className="text-xs text-amber-800 bg-amber-50 dark:text-amber-300 dark:bg-amber-900/20 rounded-md px-2 py-1.5">
          Uzamčeno uživatelem {node.lockedBy}
        </p>
      )}
    </div>
  );
};

// --- "Porady" (meetings) tab ---

interface AttachMeetingDialogProps {
  mindMapId: string;
  attachedMeetingIds: string[];
  onClose: () => void;
}

// The feature's whole premise is attaching meetings over time, so the dialog must not
// silently cap the list at the API's default page size (20) — a real user would hit
// that within weeks and the empty-state copy would then lie about there being nothing
// left to attach. A full search box is out of scope for this wave; requesting a much
// larger page is the proportionate fix.
const ATTACH_DIALOG_PAGE_SIZE = 200;

const AttachMeetingDialog: React.FC<AttachMeetingDialogProps> = ({ mindMapId, attachedMeetingIds, onClose }) => {
  const { data, isLoading, error } = useMeetingTasksList(undefined, undefined, false, 1, ATTACH_DIALOG_PAGE_SIZE);
  const attachMeeting = useAttachMeeting();
  const attachedSet = new Set(attachedMeetingIds);
  const options = (data?.items ?? []).filter((t) => !attachedSet.has(t.id));
  // totalCount can exceed items.length even after filtering already-attached meetings
  // out of the fetched page — that combination means older transcripts exist beyond
  // what was requested, and the "nothing left to attach" copy would be actively wrong.
  const isTruncated = (data?.totalCount ?? 0) > (data?.items?.length ?? 0);

  const handleAttach = async (meetingTranscriptId: string) => {
    try {
      await attachMeeting.mutateAsync({ mindMapId, meetingTranscriptId });
      toast.success("Porada připojena");
      onClose();
    } catch {
      toast.error("Připojení porady se nezdařilo");
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40" onClick={onClose}>
      <div
        className="bg-white dark:bg-graphite-surface rounded-xl shadow-lg dark:shadow-soft-dark p-6 w-full max-w-md max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold dark:text-graphite-text">Připojit poradu</h2>
          <button
            type="button"
            onClick={onClose}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto space-y-1">
          {isLoading && <p className="text-sm text-gray-500 dark:text-graphite-muted">Načítání...</p>}
          {!isLoading && error && (
            <p className="text-sm text-red-600 dark:text-red-400">Nepodařilo se načíst porady</p>
          )}
          {!isLoading && !error && options.length === 0 && !isTruncated && (
            <p className="text-sm text-gray-500 dark:text-graphite-muted">Žádné další porady k připojení</p>
          )}
          {!isLoading && !error && options.length === 0 && isTruncated && (
            <p className="text-sm text-gray-500 dark:text-graphite-muted">
              Žádná z načtených porad není k připojení — mezi staršími poradami mohou být další.
            </p>
          )}
          {!isLoading && !error && isTruncated && options.length > 0 && (
            <p className="text-xs text-amber-800 bg-amber-50 dark:text-amber-300 dark:bg-amber-900/20 rounded-md px-2 py-1.5 mb-1">
              Zobrazují se pouze nejnovější porady — starší porady v tomto seznamu nejsou.
            </p>
          )}
          {!isLoading &&
            !error &&
            options.map((t) => (
              <button
                key={t.id}
                type="button"
                data-testid="mindmap-attach-option"
                disabled={attachMeeting.isPending}
                onClick={() => handleAttach(t.id)}
                className="w-full text-left px-3 py-2 rounded-md text-sm hover:bg-gray-50 dark:hover:bg-white/5 border border-gray-200 dark:border-graphite-border disabled:opacity-50"
              >
                <div className="font-medium text-gray-900 dark:text-graphite-text">{t.subject}</div>
                <div className="text-xs text-gray-500 dark:text-graphite-muted">
                  {new Date(t.plaudCreatedAt).toLocaleDateString("cs-CZ")}
                </div>
              </button>
            ))}
        </div>
      </div>
    </div>
  );
};

interface MeetingsTabProps {
  mindMapId: string;
  meetings: AttachedMeeting[];
  isReadOnly: boolean;
  isDirty: boolean;
}

const MeetingsTab: React.FC<MeetingsTabProps> = ({ mindMapId, meetings, isReadOnly, isDirty }) => {
  const [isAttachOpen, setIsAttachOpen] = useState(false);
  const detachMeeting = useDetachMeeting();

  const handleDetach = async (meetingTranscriptId: string, subject: string) => {
    if (!window.confirm(`Odpojit poradu „${subject}“?`)) return;
    try {
      await detachMeeting.mutateAsync({ mindMapId, meetingTranscriptId });
      toast.success("Porada odpojena");
    } catch {
      toast.error("Odpojení porady se nezdařilo");
    }
  };

  const handleOpenAttach = () => {
    // Attaching kicks off a background rewrite of the whole document; doing that
    // while the user has unsaved local edits would let the AI rewrite either
    // clobber those edits or be silently discarded by them later. Make the user
    // save first.
    if (isDirty) {
      toast.error("Nejprve uložte mapu, poté můžete připojit poradu.");
      return;
    }
    setIsAttachOpen(true);
  };

  return (
    <div className="space-y-3">
      <button
        type="button"
        data-testid="mindmap-attach-button"
        disabled={isReadOnly}
        onClick={handleOpenAttach}
        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 dark:bg-graphite-accent dark:hover:bg-graphite-accent/90 disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <Plus className="w-4 h-4" />
        Připojit poradu
      </button>

      {meetings.length === 0 && (
        <p className="text-sm text-gray-500 dark:text-graphite-muted">Zatím žádné připojené porady</p>
      )}

      <ul className="space-y-2">
        {meetings.map((m) => (
          <li
            key={m.meetingTranscriptId}
            className="border border-gray-200 dark:border-graphite-border rounded-md p-2 flex items-start justify-between gap-2"
          >
            <div>
              <div className="text-sm font-medium text-gray-900 dark:text-graphite-text">{m.subject}</div>
              <div className="text-xs text-gray-500 dark:text-graphite-muted flex items-center gap-1.5 mt-0.5">
                {new Date(m.plaudCreatedAt).toLocaleDateString("cs-CZ")}
                <span
                  className={`inline-flex items-center px-1.5 py-0.5 rounded-full text-xs font-medium ${
                    m.processedAt
                      ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-300"
                      : "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300"
                  }`}
                >
                  {m.processedAt ? "Zpracováno" : "Čeká"}
                </span>
              </div>
            </div>
            <button
              type="button"
              onClick={() => handleDetach(m.meetingTranscriptId, m.subject)}
              className="text-xs text-red-600 dark:text-red-400 hover:underline shrink-0"
            >
              Odpojit
            </button>
          </li>
        ))}
      </ul>

      {isAttachOpen && (
        <AttachMeetingDialog
          mindMapId={mindMapId}
          attachedMeetingIds={meetings.map((m) => m.meetingTranscriptId)}
          onClose={() => setIsAttachOpen(false)}
        />
      )}
    </div>
  );
};

// --- "Historie" tab ---

interface HistoryTabProps {
  mindMapId: string;
  versions: MindMapVersionInfo[];
  isReadOnly: boolean;
  isDirty: boolean;
}

const HistoryTab: React.FC<HistoryTabProps> = ({ mindMapId, versions, isReadOnly, isDirty }) => {
  const restoreVersion = useRestoreMindMapVersion();

  const handleRestore = async (versionNumber: number) => {
    if (isDirty) {
      toast.error("Nejprve uložte mapu, poté ji můžete obnovit na starší verzi.");
      return;
    }
    if (!window.confirm(`Obnovit verzi ${versionNumber}? Aktuální podoba mapy bude nahrazena.`)) return;
    try {
      await restoreVersion.mutateAsync({ mindMapId, versionNumber });
      toast.success("Verze obnovena");
    } catch {
      toast.error("Obnovení verze se nezdařilo");
    }
  };

  if (versions.length === 0) {
    return <p className="text-sm text-gray-500 dark:text-graphite-muted">Zatím žádná historie verzí</p>;
  }

  return (
    <ul className="space-y-2">
      {versions.map((v) => (
        <li
          key={v.versionNumber}
          className="border border-gray-200 dark:border-graphite-border rounded-md p-2 flex items-start justify-between gap-2"
        >
          <div>
            <div className="text-sm font-medium text-gray-900 dark:text-graphite-text">Verze {v.versionNumber}</div>
            <div className="text-xs text-gray-500 dark:text-graphite-muted">
              {new Date(v.createdAt).toLocaleString("cs-CZ")} · {v.triggerMeetingSubject ?? "Ruční obnova"}
            </div>
          </div>
          <button
            type="button"
            disabled={isReadOnly || restoreVersion.isPending}
            onClick={() => handleRestore(v.versionNumber)}
            className="text-xs px-2 py-1 rounded-md border border-gray-300 dark:border-graphite-border hover:bg-gray-50 dark:hover:bg-white/5 dark:text-graphite-muted disabled:opacity-50 disabled:cursor-not-allowed shrink-0"
          >
            Obnovit
          </button>
        </li>
      ))}
    </ul>
  );
};

// --- Panel shell ---

const MindMapSidePanel: React.FC<MindMapSidePanelProps> = ({
  detail,
  document: doc,
  selectedNodeId,
  isReadOnly,
  isDirty,
  onUpdateNode,
}) => {
  const [activeTab, setActiveTab] = useState<SidePanelTab>("node");

  // Selecting a node (click or double-click on the canvas) should bring the
  // "Uzel" tab into view so the node's fields are immediately visible.
  useEffect(() => {
    if (selectedNodeId) setActiveTab("node");
  }, [selectedNodeId]);

  return (
    <div className="w-96 shrink-0 border border-gray-200 dark:border-graphite-border rounded-lg bg-white dark:bg-graphite-surface flex flex-col overflow-hidden">
      <div className="flex border-b border-gray-200 dark:border-graphite-border">
        {(Object.keys(TAB_LABELS) as SidePanelTab[]).map((tab) => (
          <button
            key={tab}
            type="button"
            onClick={() => setActiveTab(tab)}
            className={`flex-1 px-3 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeTab === tab
                ? "border-indigo-500 text-indigo-600 dark:text-graphite-accent dark:border-graphite-accent"
                : "border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted"
            }`}
          >
            {TAB_LABELS[tab]}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-y-auto p-4">
        {activeTab === "node" && (
          <NodeTab document={doc} selectedNodeId={selectedNodeId} isReadOnly={isReadOnly} onUpdateNode={onUpdateNode} />
        )}
        {activeTab === "meetings" && (
          <MeetingsTab mindMapId={detail.id} meetings={detail.meetings} isReadOnly={isReadOnly} isDirty={isDirty} />
        )}
        {activeTab === "history" && (
          <HistoryTab mindMapId={detail.id} versions={detail.versions} isReadOnly={isReadOnly} isDirty={isDirty} />
        )}
      </div>
    </div>
  );
};

export default MindMapSidePanel;
