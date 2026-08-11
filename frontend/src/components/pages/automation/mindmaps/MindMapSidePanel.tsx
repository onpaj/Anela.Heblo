import React, { useState } from "react";
import toast from "react-hot-toast";
import { PanelRightClose, PanelRightOpen, Plus, X } from "lucide-react";
import {
  AttachedMeeting,
  MindMapDetail,
  MindMapVersionInfo,
  useAttachMeeting,
  useDetachMeeting,
  useRestoreMindMapVersion,
} from "../../../../api/hooks/useMindMaps";
import { useMeetingTasksList } from "../../../../api/hooks/useMeetingTasks";

type SidePanelTab = "meetings" | "history";

const TAB_LABELS: Record<SidePanelTab, string> = {
  meetings: "Porady",
  history: "Historie",
};

export interface MindMapSidePanelProps {
  detail: MindMapDetail;
  isReadOnly: boolean;
  // Required by the "Historie" and "Porady" tabs: both refuse to act while the map
  // has unsaved edits, and only the page can know that.
  isDirty: boolean;
}

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

const MindMapSidePanel: React.FC<MindMapSidePanelProps> = ({ detail, isReadOnly, isDirty }) => {
  // Node editing moved to its own dialog, so what is left here — attached meetings
  // and version history — is reference material rather than something needed while
  // working with the map. Start out of the way.
  const [isFolded, setIsFolded] = useState(true);
  const [activeTab, setActiveTab] = useState<SidePanelTab>("meetings");

  if (isFolded) {
    return (
      <div className="flex w-10 shrink-0 flex-col items-center rounded-lg border border-gray-200 bg-white py-2 dark:border-graphite-border dark:bg-graphite-surface">
        <button
          type="button"
          data-testid="mindmap-panel-toggle"
          aria-label="Zobrazit panel"
          onClick={() => setIsFolded(false)}
          className="rounded-md p-1 text-gray-400 hover:bg-gray-50 hover:text-gray-600 dark:text-graphite-faint dark:hover:bg-white/5 dark:hover:text-graphite-muted"
        >
          <PanelRightOpen className="h-5 w-5" />
        </button>
      </div>
    );
  }

  return (
    <div className="flex w-96 shrink-0 flex-col overflow-hidden rounded-lg border border-gray-200 bg-white dark:border-graphite-border dark:bg-graphite-surface">
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
        <button
          type="button"
          data-testid="mindmap-panel-toggle"
          aria-label="Skrýt panel"
          onClick={() => setIsFolded(true)}
          className="border-b-2 border-transparent px-2 text-gray-400 hover:text-gray-600 dark:text-graphite-faint dark:hover:text-graphite-muted"
        >
          <PanelRightClose className="h-5 w-5" />
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-4">
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
