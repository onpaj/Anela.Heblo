import React, { useCallback, useEffect, useRef, useState } from "react";
import { X } from "lucide-react";
import { AttachedMeeting } from "../../../../api/hooks/useMindMaps";
import { MindMapNode, MindMapNodePatch, MindMapNodeStatus } from "./mindMapDocument";

const STATUS_LABELS: Record<MindMapNodeStatus, string> = {
  active: "Aktivní",
  done: "Hotovo",
  blocked: "Blokováno",
  idea: "Nápad",
};
const STATUS_OPTIONS = Object.keys(STATUS_LABELS) as MindMapNodeStatus[];

const INPUT_CLASS =
  "w-full px-3 py-2 rounded-md text-sm border border-gray-300 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint disabled:opacity-60 disabled:cursor-not-allowed";

const LABEL_CLASS = "block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1";

interface CommitOnBlurFieldProps {
  id: string;
  label: string;
  value: string;
  disabled: boolean;
  rows?: number;
  testId?: string;
  autoFocus?: boolean;
  onCommit: (value: string) => void;
}

/**
 * Text field that keeps its own draft and only reports on blur. Each commit reaches
 * mind-elixir's reshapeNode, which re-renders and re-lays-out the whole map — doing
 * that per keystroke makes typing visibly stutter.
 * `key`ing this component by node id is what resets the draft when a different node
 * is opened.
 */
const CommitOnBlurField: React.FC<CommitOnBlurFieldProps> = ({
  id,
  label,
  value,
  disabled,
  rows,
  testId,
  autoFocus,
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
      <label htmlFor={id} className={LABEL_CLASS}>
        {label}
      </label>
      {rows ? (
        <textarea {...props} rows={rows} />
      ) : (
        <input {...props} type="text" autoFocus={autoFocus} />
      )}
    </div>
  );
};

export interface MindMapNodeEditorDialogProps {
  node: MindMapNode;
  /** Attached meetings, used to resolve the node's provenance ids to subjects. */
  meetings: AttachedMeeting[];
  isReadOnly: boolean;
  onUpdateNode: (nodeId: string, patch: MindMapNodePatch) => void;
  onClose: () => void;
}

const MindMapNodeEditorDialog: React.FC<MindMapNodeEditorDialogProps> = ({
  node,
  meetings,
  isReadOnly,
  onUpdateNode,
  onClose,
}) => {
  // Fields commit on blur, and removing a focused element from the DOM dispatches no
  // blur event — closing straight away would silently drop whatever the user was in
  // the middle of typing. Blur first, then close.
  const closeWithFlush = useCallback(() => {
    const active = window.document.activeElement;
    if (active instanceof HTMLElement) active.blur();
    onClose();
  }, [onClose]);

  // A `click` event is dispatched on the nearest common ancestor of `mousedown` and
  // `mouseup`, not on wherever the press started — so dragging a text selection that
  // starts inside the roomy Poznámky textarea and releases over the backdrop still
  // targets the overlay's `onClick` and would close the dialog mid-selection. Only
  // treat it as a backdrop click when the press itself (mousedown) also landed on
  // the backdrop.
  const backdropPressRef = useRef(false);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      closeWithFlush();
    };
    window.document.addEventListener("keydown", onKeyDown);
    return () => window.document.removeEventListener("keydown", onKeyDown);
  }, [closeWithFlush]);

  const sourceMeetings = node.sourceMeetingIds.map((id) => ({
    id,
    meeting: meetings.find((m) => m.meetingTranscriptId === id) ?? null,
  }));

  return (
    <div
      data-testid="mindmap-node-editor"
      role="dialog"
      aria-modal="true"
      aria-labelledby="mindmap-node-editor-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      onMouseDown={(e) => {
        backdropPressRef.current = e.target === e.currentTarget;
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget && backdropPressRef.current) closeWithFlush();
      }}
    >
      <div
        className="flex max-h-[85vh] w-full max-w-3xl flex-col overflow-hidden rounded-xl bg-white shadow-lg dark:bg-graphite-surface dark:shadow-soft-dark"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3 dark:border-graphite-border">
          <h2 id="mindmap-node-editor-title" className="text-sm font-semibold dark:text-graphite-text">
            Detail uzlu
          </h2>
          <button
            type="button"
            onClick={closeWithFlush}
            aria-label="Zavřít detail uzlu"
            className="text-gray-400 hover:text-gray-600 dark:text-graphite-faint dark:hover:text-graphite-muted"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="space-y-4 overflow-y-auto px-5 py-4">
          <CommitOnBlurField
            key={`${node.id}-title`}
            id="mindmap-node-title"
            label="Název"
            testId="mindmap-node-title-input"
            value={node.title}
            disabled={isReadOnly}
            autoFocus
            onCommit={(title) => onUpdateNode(node.id, { title })}
          />

          <CommitOnBlurField
            key={`${node.id}-notes`}
            id="mindmap-node-notes"
            label="Poznámky"
            rows={10}
            value={node.notes ?? ""}
            disabled={isReadOnly}
            onCommit={(notes) => onUpdateNode(node.id, { notes: notes || null })}
          />

          <div className="grid grid-cols-2 gap-4">
            <CommitOnBlurField
              key={`${node.id}-owner`}
              id="mindmap-node-owner"
              label="Vlastník"
              value={node.owner ?? ""}
              disabled={isReadOnly}
              onCommit={(owner) => onUpdateNode(node.id, { owner: owner || null })}
            />

            <div>
              <label htmlFor="mindmap-node-status" className={LABEL_CLASS}>
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
          </div>

          {sourceMeetings.length > 0 && (
            <div>
              <h3 className={LABEL_CLASS}>Z porad</h3>
              <ul className="space-y-1">
                {sourceMeetings.map(({ id, meeting }) => (
                  <li
                    key={id}
                    className="rounded-md border border-gray-200 px-2 py-1.5 text-sm dark:border-graphite-border"
                  >
                    {meeting ? (
                      <>
                        <span className="text-gray-900 dark:text-graphite-text">{meeting.subject}</span>
                        <span className="ml-2 text-xs text-gray-500 dark:text-graphite-muted">
                          {new Date(meeting.plaudCreatedAt).toLocaleDateString("cs-CZ")}
                        </span>
                      </>
                    ) : (
                      // The document keeps the id after the meeting is detached.
                      <span className="text-gray-500 dark:text-graphite-muted">Odpojená porada</span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}

          {node.lockedBy && (
            <p className="rounded-md bg-amber-50 px-2 py-1.5 text-xs text-amber-800 dark:bg-amber-900/20 dark:text-amber-300">
              Uzamčeno uživatelem {node.lockedBy}
            </p>
          )}
        </div>

        <div className="border-t border-gray-200 px-5 py-3 dark:border-graphite-border">
          <button
            type="button"
            onClick={closeWithFlush}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 dark:bg-graphite-accent dark:hover:bg-graphite-accent/90"
          >
            Zavřít
          </button>
        </div>
      </div>
    </div>
  );
};

export default MindMapNodeEditorDialog;
