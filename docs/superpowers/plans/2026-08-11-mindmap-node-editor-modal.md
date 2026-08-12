# Mind map node editor modal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move mind map node editing out of the cramped side panel into a wide modal opened by double-clicking a node, and make the side panel foldable (folded by default).

**Architecture:** A new presentational `MindMapNodeEditorDialog` owns the node fields (moved verbatim from the side panel's deleted "Uzel" tab) plus a read-only provenance list. `MindMapCanvas` replaces `mind-elixir`'s `beginEdit` method so a double-click raises `onOpenNodeEditor(nodeId)` instead of opening the library's inline text box, and re-adds F2 inline renaming through a document-level capture listener. `MindMapDetailPage` holds the `editingNodeId` state and wires the three together.

**Tech Stack:** React 18 + TypeScript (CRA), Tailwind, `mind-elixir` 5.15.1, Jest + React Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-11-mindmap-node-editor-modal-design.md`

## Global Constraints

- All user-facing copy is **Czech**, with correct diacritics. Existing strings are moved verbatim, not retranslated.
- Every file lives in `frontend/src/components/pages/automation/mindmaps/`; tests in its `__tests__/` subfolder.
- Dark mode is mandatory: every colour utility needs its `dark:` counterpart, copied from the component the markup came from.
- Run tests with `CI=true npx react-scripts test --watchAll=false <path>` from `frontend/`. **Never `npx jest`** — it fails to parse the project's TypeScript.
- Lint gate is `npm run lint` from `frontend/`. `testing-library/no-node-access` is an **error**: any `querySelector`/`parentNode` in a test needs an `// eslint-disable-next-line testing-library/no-node-access` with a one-line reason, matching the convention already in `MindMapCanvas.test.tsx`.
- Build gate is `CI=false npm run build` from `frontend/`. `npx tsc --noEmit` false-greens on this project and must not be used as the type check.
- Commit messages: `<type>: <description>`, no attribution footer.

---

### Task 1: The node editor dialog

**Files:**
- Create: `frontend/src/components/pages/automation/mindmaps/MindMapNodeEditorDialog.tsx`
- Create: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapNodeEditorDialog.test.tsx`
- Modify: `frontend/src/components/pages/automation/mindmaps/mindMapDocument.ts` (add the shared patch type)
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx:18` (import the patch type instead of declaring it)

**Interfaces:**
- Consumes: `MindMapNode`, `MindMapNodeStatus` from `./mindMapDocument`; `AttachedMeeting` from `../../../../api/hooks/useMindMaps`.
- Produces:
  - `MindMapNodePatch = Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>` exported from `./mindMapDocument` (Tasks 3 and 4 rely on it; `MindMapCanvas` re-exports it so its existing importers keep working).
  - Default export `MindMapNodeEditorDialog` with `MindMapNodeEditorDialogProps { node: MindMapNode; meetings: AttachedMeeting[]; isReadOnly: boolean; onUpdateNode: (nodeId: string, patch: MindMapNodePatch) => void; onClose: () => void }`.
  - Test ids: `mindmap-node-editor` (dialog root), `mindmap-node-title-input`.

**Why the dialog and not the panel:** the "Uzel" tab gave Poznámky four rows inside a 384px column. Everything else here is a straight move of code that already exists in `MindMapSidePanel.tsx:15-171`.

- [ ] **Step 1: Move the patch type into `mindMapDocument.ts`**

The dialog must not import from `MindMapCanvas.tsx` — that would pull `mind-elixir` into its module graph and force every dialog test to mock the library. Append to `frontend/src/components/pages/automation/mindmaps/mindMapDocument.ts`:

```ts
/** The subset of a node the user can edit by hand. */
export type MindMapNodePatch = Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>;
```

In `MindMapCanvas.tsx`, delete the local declaration on line 18 and re-export the shared one. The import on line 7 becomes:

```ts
import { MindMapDocument, MindMapNodePatch } from "./mindMapDocument";

export type { MindMapNodePatch };
```

`MindMapNode` is no longer referenced in that file, so dropping it from the import is required — an unused import fails lint.

- [ ] **Step 2: Write the failing tests**

Create `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapNodeEditorDialog.test.tsx`:

```tsx
import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import MindMapNodeEditorDialog, { MindMapNodeEditorDialogProps } from "../MindMapNodeEditorDialog";
import { AttachedMeeting } from "../../../../../api/hooks/useMindMaps";
import { MindMapNode } from "../mindMapDocument";

function buildNode(overrides: Partial<MindMapNode> = {}): MindMapNode {
  return {
    id: "a",
    parentId: "root",
    title: "Větev A",
    notes: null,
    status: "active",
    owner: null,
    lockedBy: null,
    sourceMeetingIds: [],
    position: null,
    collapsed: false,
    ...overrides,
  };
}

function buildMeeting(overrides: Partial<AttachedMeeting> = {}): AttachedMeeting {
  return {
    meetingTranscriptId: "m-1",
    subject: "Porada 1. srpna",
    plaudCreatedAt: "2026-08-01T09:00:00Z",
    attachedAt: "2026-08-01T10:00:00Z",
    processedAt: "2026-08-01T10:05:00Z",
    ...overrides,
  };
}

function renderDialog(overrides: Partial<MindMapNodeEditorDialogProps> = {}) {
  return render(
    <MindMapNodeEditorDialog
      node={buildNode()}
      meetings={[]}
      isReadOnly={false}
      onUpdateNode={jest.fn()}
      onClose={jest.fn()}
      {...overrides}
    />,
  );
}

describe("MindMapNodeEditorDialog", () => {
  it("does not push a document change on every keystroke in the title field", () => {
    const onUpdateNode = jest.fn();
    renderDialog({ onUpdateNode });

    const input = screen.getByTestId("mindmap-node-title-input");
    fireEvent.change(input, { target: { value: "Nov" } });
    fireEvent.change(input, { target: { value: "Nový" } });

    // Each keystroke would otherwise trigger a full mind-elixir re-layout.
    expect(onUpdateNode).not.toHaveBeenCalled();
    expect(input).toHaveValue("Nový");
  });

  it("commits the title when the field loses focus", () => {
    const onUpdateNode = jest.fn();
    renderDialog({ onUpdateNode });

    const input = screen.getByTestId("mindmap-node-title-input");
    fireEvent.change(input, { target: { value: "Nový název" } });
    fireEvent.blur(input);

    expect(onUpdateNode).toHaveBeenCalledWith("a", { title: "Nový název" });
  });

  it("does not commit when the text is unchanged", () => {
    const onUpdateNode = jest.fn();
    renderDialog({ onUpdateNode });
    fireEvent.blur(screen.getByTestId("mindmap-node-title-input"));
    expect(onUpdateNode).not.toHaveBeenCalled();
  });

  it("still commits status immediately — a select has no intermediate states", () => {
    const onUpdateNode = jest.fn();
    renderDialog({ onUpdateNode });
    fireEvent.change(screen.getByLabelText("Stav"), { target: { value: "done" } });
    expect(onUpdateNode).toHaveBeenCalledWith("a", { status: "done" });
  });

  it("commits the field that still has focus before closing", () => {
    // Fields commit on blur, and unmounting a focused field dispatches no blur
    // event — without an explicit flush the last thing typed is silently lost.
    const onUpdateNode = jest.fn();
    const onClose = jest.fn();
    renderDialog({ onUpdateNode, onClose });

    const input = screen.getByTestId("mindmap-node-title-input");
    input.focus();
    fireEvent.change(input, { target: { value: "Rozepsaný název" } });
    fireEvent.click(screen.getByRole("button", { name: "Zavřít" }));

    expect(onUpdateNode).toHaveBeenCalledWith("a", { title: "Rozepsaný název" });
    expect(onClose).toHaveBeenCalled();
  });

  it("closes on Escape, flushing the focused field the same way", () => {
    const onUpdateNode = jest.fn();
    const onClose = jest.fn();
    renderDialog({ onUpdateNode, onClose });

    const input = screen.getByTestId("mindmap-node-title-input");
    input.focus();
    fireEvent.change(input, { target: { value: "Rozepsáno" } });
    fireEvent.keyDown(window.document, { key: "Escape" });

    expect(onUpdateNode).toHaveBeenCalledWith("a", { title: "Rozepsáno" });
    expect(onClose).toHaveBeenCalled();
  });

  it("shows each node's own values, and an abandoned draft does not leak between nodes", () => {
    // The draft is reset by keying each field on the node id. Rerendering the SAME
    // tree (not unmount + fresh render) is what exercises that: unmounting would
    // re-initialise useState(value) from props regardless of the `key`, making the
    // test pass even with the key removed.
    const { rerender } = renderDialog();
    fireEvent.change(screen.getByTestId("mindmap-node-title-input"), { target: { value: "rozepsáno" } });

    rerender(
      <MindMapNodeEditorDialog
        node={buildNode({ id: "b", title: "List B" })}
        meetings={[]}
        isReadOnly={false}
        onUpdateNode={jest.fn()}
        onClose={jest.fn()}
      />,
    );
    expect(screen.getByTestId("mindmap-node-title-input")).toHaveValue("List B");
  });

  it("lists the meetings the node came from", () => {
    renderDialog({
      node: buildNode({ sourceMeetingIds: ["m-1"] }),
      meetings: [buildMeeting()],
    });
    expect(screen.getByText("Porada 1. srpna")).toBeInTheDocument();
  });

  it("marks a source meeting that is no longer attached", () => {
    // The id survives in the document after the meeting is detached; claiming the
    // node has no provenance would be a lie.
    renderDialog({ node: buildNode({ sourceMeetingIds: ["m-gone"] }), meetings: [] });
    expect(screen.getByText("Odpojená porada")).toBeInTheDocument();
  });

  it("omits the provenance section entirely for a hand-made node", () => {
    renderDialog();
    expect(screen.queryByText("Z porad")).not.toBeInTheDocument();
  });

  it("shows the lock notice only for a locked node", () => {
    const { unmount } = renderDialog();
    expect(screen.queryByText(/Uzamčeno uživatelem/)).not.toBeInTheDocument();
    unmount();

    renderDialog({ node: buildNode({ lockedBy: "ondra@anela.cz" }) });
    expect(screen.getByText(/Uzamčeno uživatelem ondra@anela.cz/)).toBeInTheDocument();
  });

  it("disables every field while the map is read-only", () => {
    renderDialog({ isReadOnly: true });
    expect(screen.getByTestId("mindmap-node-title-input")).toBeDisabled();
    expect(screen.getByLabelText("Poznámky")).toBeDisabled();
    expect(screen.getByLabelText("Vlastník")).toBeDisabled();
    expect(screen.getByLabelText("Stav")).toBeDisabled();
  });
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run from `frontend/`:
```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapNodeEditorDialog.test.tsx
```
Expected: FAIL — `Cannot find module '../MindMapNodeEditorDialog'`.

- [ ] **Step 4: Write the dialog**

Create `frontend/src/components/pages/automation/mindmaps/MindMapNodeEditorDialog.tsx`:

```tsx
import React, { useCallback, useEffect, useState } from "react";
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
      {rows ? <textarea {...props} rows={rows} /> : <input {...props} type="text" />}
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
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      onClick={closeWithFlush}
    >
      <div
        className="flex max-h-[85vh] w-full max-w-3xl flex-col overflow-hidden rounded-xl bg-white shadow-lg dark:bg-graphite-surface dark:shadow-soft-dark"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3 dark:border-graphite-border">
          <h2 className="text-sm font-semibold dark:text-graphite-text">Detail uzlu</h2>
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
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
          >
            Zavřít
          </button>
        </div>
      </div>
    </div>
  );
};

export default MindMapNodeEditorDialog;
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapNodeEditorDialog.test.tsx
```
Expected: PASS, 12 tests.

If "commits the field that still has focus before closing" fails, `closeWithFlush` is calling `onClose()` before `blur()` — the order is what the test is for.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapNodeEditorDialog.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapNodeEditorDialog.test.tsx \
        frontend/src/components/pages/automation/mindmaps/mindMapDocument.ts \
        frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx
git commit -m "feat: add a wide modal editor for mind map nodes"
```

---

### Task 2: Fold the side panel and drop its node tab

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapSidePanel.tsx`
- Modify: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapSidePanel.test.tsx`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `MindMapSidePanelProps { detail: MindMapDetail; isReadOnly: boolean; isDirty: boolean }` — the `document`, `selectedNodeId` and `onUpdateNode` props are **gone**, which Task 4 must match. New test id `mindmap-panel-toggle` on both the fold and unfold buttons.

- [ ] **Step 1: Write the failing tests**

In `__tests__/MindMapSidePanel.test.tsx`:

1. Delete the five node-field tests on lines 150-208 (`does not push a document change on every keystroke…`, `commits the title when the field loses focus`, `does not commit when the text is unchanged`, `shows each node's own values…`, `still commits status immediately…`). They now live in `MindMapNodeEditorDialog.test.tsx`.
2. Delete the now-unused `docWithChildren` helper and the `MindMapNode` import.
3. Change `renderPanel` to the reduced prop set and unfold by default, so the existing meeting/history tests keep working:

```tsx
function renderPanel(overrides: Partial<MindMapSidePanelProps> = {}) {
  const utils = render(
    <MindMapSidePanel detail={buildDetail()} isReadOnly={false} isDirty={false} {...overrides} />,
  );
  // The panel ships folded; every tab assertion below needs it open.
  fireEvent.click(screen.getByTestId("mindmap-panel-toggle"));
  return utils;
}
```

4. Add the folding tests:

```tsx
  it("starts folded, so the map gets the full width", () => {
    render(<MindMapSidePanel detail={buildDetail()} isReadOnly={false} isDirty={false} />);

    expect(screen.getByTestId("mindmap-panel-toggle")).toHaveAttribute("aria-label", "Zobrazit panel");
    expect(screen.queryByText("Porady")).not.toBeInTheDocument();
    expect(screen.queryByText("Historie")).not.toBeInTheDocument();
  });

  it("shows the tabs once unfolded and hides them again when folded back", () => {
    render(<MindMapSidePanel detail={buildDetail()} isReadOnly={false} isDirty={false} />);

    fireEvent.click(screen.getByTestId("mindmap-panel-toggle"));
    expect(screen.getByText("Porady")).toBeInTheDocument();
    expect(screen.getByText("Historie")).toBeInTheDocument();

    fireEvent.click(screen.getByTestId("mindmap-panel-toggle"));
    expect(screen.queryByText("Porady")).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapSidePanel.test.tsx
```
Expected: FAIL — `Unable to find an element by: [data-testid="mindmap-panel-toggle"]`.

- [ ] **Step 3: Rewrite the panel shell**

In `MindMapSidePanel.tsx`:

1. Delete the `NodeTab` component, the `CommitOnBlurField` component, `STATUS_LABELS`, `STATUS_OPTIONS`, `INPUT_CLASS`, and the `MindMapDocument`/`MindMapNode`/`MindMapNodeStatus` import — all of it moved to the dialog in Task 1. `AttachMeetingDialog` keeps its own input-free markup, so nothing else needs `INPUT_CLASS`.
2. Change the tab type and labels:

```tsx
type SidePanelTab = "meetings" | "history";

const TAB_LABELS: Record<SidePanelTab, string> = {
  meetings: "Porady",
  history: "Historie",
};
```

3. Replace the props interface:

```tsx
export interface MindMapSidePanelProps {
  detail: MindMapDetail;
  isReadOnly: boolean;
  // Required by the "Historie" and "Porady" tabs: both refuse to act while the map
  // has unsaved edits, and only the page can know that.
  isDirty: boolean;
}
```

4. Replace the shell component (and drop the `useEffect` import — nothing uses it once the select-a-node-shows-the-Uzel-tab effect is gone):

```tsx
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
```

5. Update the lucide import on line 3:

```tsx
import { PanelRightClose, PanelRightOpen, Plus, X } from "lucide-react";
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapSidePanel.test.tsx
```
Expected: PASS, 5 tests (3 surviving guard tests + 2 folding tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapSidePanel.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapSidePanel.test.tsx
git commit -m "feat: fold the mind map side panel and drop its node tab"
```

---

### Task 3: Open the editor from the canvas

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx`
- Modify: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx`

**Interfaces:**
- Consumes: `MindMapNodePatch` from `./mindMapDocument` (moved in Task 1).
- Produces: a new **required** prop on `MindMapCanvasProps`: `onOpenNodeEditor: (nodeId: string) => void`. Task 4 must pass it.

**Background — read before writing code.** `mind-elixir` 5.15.1 does not dispatch or listen for a DOM `dblclick` event. Its pointer handler detects double taps itself and calls `instance.beginEdit(el)`; F2 goes through the same method. Verify for yourself in `frontend/node_modules/mind-elixir/dist/MindElixir.js`: the double-tap branch is `d = (f) => { if (!e.editable) return; ... e.selectNode(b), e.beginEdit(b) }` around line 1134, dispatched from `s.detect(f, d)` in the `pointerup` handler around line 1194; the F2 entry is around line 938; the key map is bound as `e.container.onkeydown` around line 998. There is no constructor option that turns inline editing off, so replacing the method is the only available seam.

- [ ] **Step 1: Write the failing tests**

In `__tests__/MindMapCanvas.test.tsx`:

1. Add `beginEdit: jest.fn(),` to the stub `instance` object (after `undo`). Without it, the component's `instance.beginEdit.bind(instance)` throws on mount and **every** test in the file fails.
2. Add `onOpenNodeEditor={jest.fn()}` to `renderCanvas`'s default props.
3. Append these tests inside `describe("MindMapCanvas", …)`:

```tsx
  it("opens the node editor instead of mind-elixir's inline text box", () => {
    // mind-elixir's own double-tap handler calls beginEdit(topicElement); the
    // component replaces that method, which is the only seam the library offers.
    const onOpenNodeEditor = jest.fn();
    renderCanvas({ onOpenNodeEditor });

    act(() => {
      (instance.beginEdit as jest.Mock)({ nodeObj: { id: "a" } });
    });

    expect(onOpenNodeEditor).toHaveBeenCalledWith("a");
  });

  it("falls back to the selected node when beginEdit is called with no element", () => {
    const onOpenNodeEditor = jest.fn();
    instance.currentNode = { nodeObj: { id: "b" } };
    renderCanvas({ onOpenNodeEditor });

    act(() => {
      (instance.beginEdit as jest.Mock)();
    });

    expect(onOpenNodeEditor).toHaveBeenCalledWith("b");
    instance.currentNode = null;
  });

  it("keeps F2 on mind-elixir's own inline editor", () => {
    // Captured before the component replaces the method on mount.
    const inlineEditor = instance.beginEdit as jest.Mock;
    const onOpenNodeEditor = jest.fn();
    renderCanvas({ onOpenNodeEditor });

    fireEvent.keyDown(screen.getByTestId("mindmap-canvas"), { key: "F2" });

    expect(inlineEditor).toHaveBeenCalled();
    expect(onOpenNodeEditor).not.toHaveBeenCalled();
  });

  it("ignores F2 pressed outside the map", () => {
    const inlineEditor = instance.beginEdit as jest.Mock;
    renderCanvas();

    fireEvent.keyDown(window.document.body, { key: "F2" });

    expect(inlineEditor).not.toHaveBeenCalled();
  });

  it("opens the node editor on a real double-click while the map is read-only", () => {
    // mind-elixir's double-tap path bails at `if (!e.editable) return`, so the
    // replaced beginEdit never fires while the map is Updating.
    const onOpenNodeEditor = jest.fn();
    renderCanvas({ isReadOnly: true, onOpenNodeEditor });

    const canvas = screen.getByTestId("mindmap-canvas");
    const topic = window.document.createElement("me-tpc");
    (topic as HTMLElement & { nodeObj: { id: string } }).nodeObj = { id: "a" };
    canvas.appendChild(topic);

    fireEvent.dblClick(topic);

    expect(onOpenNodeEditor).toHaveBeenCalledWith("a");
  });

  it("restores mind-elixir's own beginEdit when it unmounts", () => {
    const inlineEditor = instance.beginEdit;
    const { unmount } = renderCanvas();
    expect(instance.beginEdit).not.toBe(inlineEditor);

    unmount();

    expect(instance.beginEdit).toBe(inlineEditor);
  });
```

Add `fireEvent` to the `@testing-library/react` import on line 2.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx
```
Expected: FAIL — the six new tests fail because `onOpenNodeEditor` is never called and `instance.beginEdit` is never replaced. The pre-existing tests must still pass.

- [ ] **Step 3: Add the prop and the ref plumbing**

In `MindMapCanvas.tsx`, extend the props interface (after `onSelectNode`):

```tsx
  /** A double-click (or F2's replacement) asks the page to open the node editor. */
  onOpenNodeEditor: (nodeId: string) => void;
```

Destructure it in the component signature, add it to the latest-callbacks block so the mount effect can stay dependency-free, and add a read-only ref:

```tsx
  const onChangeRef = useRef(onChange);
  const onSelectNodeRef = useRef(onSelectNode);
  const onOpenNodeEditorRef = useRef(onOpenNodeEditor);
  const isReadOnlyRef = useRef(isReadOnly);
  useEffect(() => {
    onChangeRef.current = onChange;
    onSelectNodeRef.current = onSelectNode;
    onOpenNodeEditorRef.current = onOpenNodeEditor;
    isReadOnlyRef.current = isReadOnly;
  }, [onChange, onSelectNode, onOpenNodeEditor, isReadOnly]);
```

Add the `Topic` type to the mind-elixir type import on line 3:

```tsx
import type { MindElixirInstance, NodeObj, Topic } from "mind-elixir";
```

- [ ] **Step 4: Replace `beginEdit` and re-add F2**

Inside the mount effect, immediately after `instanceRef.current = instance;`:

```tsx
    // mind-elixir has no DOM `dblclick` event to intercept: it detects double taps
    // itself inside its pointerup handler and calls instance.beginEdit (verified in
    // dist/MindElixir.js — the double-tap branch bails at `if (!e.editable) return`
    // and then does `selectNode(b), beginEdit(b)`). There is no option that turns
    // inline editing off, so replacing the method is the only seam: it swaps the
    // library's inline #input-box for our own editor dialog.
    const inlineBeginEdit = instance.beginEdit.bind(instance);
    instance.beginEdit = ((el?: Topic) => {
      const target = el ?? instance.currentNode;
      if (target) onOpenNodeEditorRef.current(target.nodeObj.id);
      return Promise.resolve();
    }) as MindElixirInstance["beginEdit"];

    // F2 must still start inline typing, and it reaches the same beginEdit. The
    // library binds its key map as `container.onkeydown`, and the container is
    // normally the key event's own target — where capture and bubble listeners fire
    // in registration order, so a capture listener on the container itself is not
    // guaranteed to win. Intercept at the document, where the capture phase always
    // runs first, and stop the event before the library's own handler sees it.
    const handleF2 = (event: KeyboardEvent) => {
      if (event.key !== "F2" || isReadOnlyRef.current) return;
      const target = event.target;
      if (!(target instanceof Node) || !container.contains(target)) return;
      event.stopPropagation();
      void inlineBeginEdit();
    };
    window.document.addEventListener("keydown", handleF2, true);

    // While the map is read-only the library's double-tap path returns before
    // reaching beginEdit, so the replacement above never fires and the node detail
    // would be unreachable. Listen for the browser's own dblclick as well. On an
    // editable map both paths run and both call onOpenNodeEditor with the same id,
    // which the page turns into the same state — a harmless duplicate.
    const handleDoubleClick = (event: MouseEvent) => {
      const target = event.target;
      if (!(target instanceof HTMLElement)) return;
      const topic = target.closest("me-tpc") as Topic | null;
      if (topic?.nodeObj) onOpenNodeEditorRef.current(topic.nodeObj.id);
    };
    container.addEventListener("dblclick", handleDoubleClick);
```

In the same effect's cleanup, before `instance.destroy()`:

```tsx
      window.document.removeEventListener("keydown", handleF2, true);
      container.removeEventListener("dblclick", handleDoubleClick);
      instance.beginEdit = inlineBeginEdit as MindElixirInstance["beginEdit"];
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx
```
Expected: PASS, 30 tests (24 pre-existing + 6 new).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx
git commit -m "feat: open the node editor on double-click instead of inline editing"
```

---

### Task 4: Wire the page together

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx`
- Modify: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapDetailPage.test.tsx`

**Interfaces:**
- Consumes: `MindMapNodeEditorDialog` (Task 1), the reduced `MindMapSidePanelProps` (Task 2), `onOpenNodeEditor` on `MindMapCanvasProps` (Task 3).
- Produces: the finished feature. New canvas-stub test id `stub-open-editor`.

- [ ] **Step 1: Write the failing tests**

In `__tests__/MindMapDetailPage.test.tsx`:

1. Give the canvas stub the new prop and a button that fires it. In the stub's prop type add `onOpenNodeEditor: (id: string) => void;`, and add this button next to `stub-edit`:

```tsx
            <button
              type="button"
              data-testid="stub-open-editor"
              onClick={() => props.onOpenNodeEditor("root")}
            >
              open editor
            </button>
```

2. Repoint the read-only test (line 295, `disables the panel and save controls while the map is Updating`). Replace its body's last three assertions with:

```tsx
    expect(screen.getByTestId("mindmap-status-badge")).toHaveTextContent("Aktualizuje se");
    expect(screen.getByTestId("mindmap-save-button")).toBeDisabled();

    fireEvent.click(screen.getByTestId("stub-open-editor"));
    expect(screen.getByTestId("mindmap-node-title-input")).toBeDisabled();
    expect(screen.getByText(/Mapa se právě aktualizuje/)).toBeInTheDocument();
```

`selectRootNode()` is no longer what opens the fields, but leave the call in place — it still exercises the selection path.

3. Add two tests:

```tsx
  it("opens the node editor for the node the canvas reports, and closes it again", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) return jsonResponse(buildDetail());
      throw new Error(`Unexpected fetch: ${url}`);
    });

    renderPage(newQueryClient());
    await screen.findByTestId("mindmap-canvas-stub");

    expect(screen.queryByTestId("mindmap-node-editor")).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId("stub-open-editor"));
    expect(screen.getByTestId("mindmap-node-title-input")).toHaveValue("Projekt");

    fireEvent.click(screen.getByRole("button", { name: "Zavřít" }));
    expect(screen.queryByTestId("mindmap-node-editor")).not.toBeInTheDocument();
  });

  it("commits the focused editor field before ⌘S reads the document", async () => {
    // The dialog's fields commit on blur; ⌘S while one has focus would otherwise
    // save a document that never received the last thing typed.
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    mockCanvasHandle.getDocument.mockReturnValue(buildDoc());
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? "GET").toUpperCase();
      if (method === "GET" && url === DETAIL_URL) return jsonResponse(buildDetail());
      if (method === "PUT" && url === SAVE_URL) return jsonResponse({ documentJson: JSON.stringify(buildDoc()) });
      throw new Error(`Unexpected fetch: ${method} ${url}`);
    });

    renderPage(newQueryClient());
    await screen.findByTestId("mindmap-canvas-stub");

    fireEvent.click(screen.getByTestId("stub-open-editor"));
    const input = screen.getByTestId("mindmap-node-title-input");
    input.focus();
    fireEvent.change(input, { target: { value: "Upravený název" } });

    fireEvent.keyDown(window.document, { key: "s", metaKey: true });

    await waitFor(() =>
      expect(mockCanvasHandle.patchNode).toHaveBeenCalledWith("root", { title: "Upravený název" }),
    );
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapDetailPage.test.tsx
```
Expected: FAIL — `mindmap-node-title-input` is never found; the page still renders the old side panel.

- [ ] **Step 3: Wire the page**

In `MindMapDetailPage.tsx`:

1. Import the dialog next to the other mind map imports:

```tsx
import MindMapNodeEditorDialog from "./MindMapNodeEditorDialog";
```

2. Rename `panelDoc`/`setPanelDoc` to `canvasDoc`/`setCanvasDoc` throughout (declaration on line 41, plus the four `setPanelDoc(...)` call sites in the adoption effect, `handleCanvasChange`, `handleSelectNode` and `handleSave`) — it now feeds the dialog, not the panel. Add the editing state next to it:

```tsx
  const [editingNodeId, setEditingNodeId] = useState<string | null>(null);
```

3. Add the open handler beside `handleSelectNode`:

```tsx
  const handleOpenNodeEditor = useCallback((nodeId: string) => {
    setEditingNodeId(nodeId);
    // Which selection events mind-elixir fired before the double-click is its own
    // business; pull a snapshot so the dialog always opens on current values.
    const snapshot = canvasRef.current?.getDocument();
    if (snapshot) setCanvasDoc(snapshot);
  }, []);
```

4. Flush the focused field at the top of `handleSave`, before reading the document:

```tsx
  const handleSave = useCallback(async (): Promise<boolean> => {
    // A dialog field commits on blur. ⌘S while one has focus would otherwise read
    // the document before that commit lands. reshapeNode is synchronous, so the
    // flush is visible to getDocument on the next line.
    const active = window.document.activeElement;
    if (active instanceof HTMLElement) active.blur();

    const documentToSave = canvasRef.current?.getDocument();
```

5. Derive the node being edited, next to `hasNewerServerVersion` (after the early returns, so `detail` is non-null):

```tsx
  const editingNode = editingNodeId ? canvasDoc?.nodes.find((n) => n.id === editingNodeId) ?? null : null;
```

6. Pass the new prop to the canvas, inside the `<MindMapCanvas …>` element:

```tsx
                onOpenNodeEditor={handleOpenNodeEditor}
```

7. Replace the side panel block (lines 334-343) with the reduced props, dropping the `panelDoc` guard — the panel needs only `detail`, and the parse-error early return above guarantees `loadedDoc` exists here anyway:

```tsx
        <MindMapSidePanel detail={detail} isReadOnly={isReadOnly} isDirty={isDirty} />
```

8. Render the dialog next to `MindMapHelpSheet`, just above `<UnsavedChangesDialog … />`:

```tsx
      {editingNode && (
        <MindMapNodeEditorDialog
          node={editingNode}
          meetings={detail.meetings}
          isReadOnly={isReadOnly}
          onUpdateNode={handleUpdateNode}
          onClose={() => setEditingNodeId(null)}
        />
      )}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps/__tests__/MindMapDetailPage.test.tsx
```
Expected: PASS, 12 tests (10 pre-existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapDetailPage.test.tsx
git commit -m "feat: wire the mind map node editor dialog into the detail page"
```

---

### Task 5: Update the help sheet and validate the whole change

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapHelpSheet.tsx:12`

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: nothing further.

- [ ] **Step 1: Split the double-click row**

The sheet claims `dvojklik / F2 → psát do uzlu`, which is now only half true. In `MindMapHelpSheet.tsx`, replace that single entry in `SHORTCUTS` with two:

```tsx
  ["dvojklik", "otevřít detail uzlu"],
  ["F2", "psát do uzlu"],
```

- [ ] **Step 2: Run every mind map test**

```bash
CI=true npx react-scripts test --watchAll=false src/components/pages/automation/mindmaps
```
Expected: PASS — 8 suites, no failures.

- [ ] **Step 3: Lint**

```bash
npm run lint
```
Expected: no errors. If `testing-library/no-node-access` fires on the `canvas.appendChild(topic)` line in `MindMapCanvas.test.tsx`, add the disable comment with a reason, matching the convention already used twice in that file.

- [ ] **Step 4: Build**

```bash
CI=false npm run build
```
Expected: `Compiled successfully` (warnings are acceptable; type errors are not). This is the real type gate — `npx tsc --noEmit` false-greens on this project.

- [ ] **Step 5: Check it in the browser**

Start the app, open a mind map with at least one attached meeting, and confirm:
- the side panel starts folded, unfolds to Porady/Historie, and folds back;
- double-clicking a node opens the wide dialog instead of the inline text box, with a roomy Poznámky area;
- F2 on a selected node still types inside the node;
- editing a field and closing the dialog leaves the map dirty and ⌘S saves the edit;
- a node created from a meeting shows its meeting under "Z porad";
- both light and dark themes look right.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapHelpSheet.tsx
git commit -m "docs: tell the help sheet that double-click now opens the node detail"
```
