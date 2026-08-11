import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import toast from "react-hot-toast";
import MindMapSidePanel, { MindMapSidePanelProps } from "../MindMapSidePanel";
import { MindMapDetail, useAttachMeeting, useDetachMeeting, useRestoreMindMapVersion } from "../../../../../api/hooks/useMindMaps";
import { useMeetingTasksList } from "../../../../../api/hooks/useMeetingTasks";
import { MindMapDocument, MindMapNode } from "../mindMapDocument";

// These two mutation-gated flows (attach, restore) exist specifically to stop the
// background Claude rewrite from clobbering or silently discarding unsaved local
// edits — see MindMapSidePanel.tsx's handleOpenAttach/handleRestore. Mock the hooks
// so the tests assert directly on "was the mutation ever invoked", independent of
// network/query-client plumbing.
jest.mock("react-hot-toast", () => ({
  __esModule: true,
  default: { success: jest.fn(), error: jest.fn() },
}));

jest.mock("../../../../../api/hooks/useMindMaps", () => ({
  __esModule: true,
  useAttachMeeting: jest.fn(),
  useDetachMeeting: jest.fn(),
  useRestoreMindMapVersion: jest.fn(),
}));

jest.mock("../../../../../api/hooks/useMeetingTasks", () => ({
  __esModule: true,
  useMeetingTasksList: jest.fn(),
}));

function buildDoc(): MindMapDocument {
  return {
    schemaVersion: 1,
    rootNodeId: "root",
    nodes: [
      {
        id: "root",
        parentId: null,
        title: "Projekt",
        notes: null,
        status: "active",
        owner: null,
        lockedBy: null,
        sourceMeetingIds: [],
        position: null,
        collapsed: false,
      },
    ],
    suppressedNodes: [],
  };
}

// buildDoc() has only the root node; these tests need two selectable siblings.
function docWithChildren(): MindMapDocument {
  const base = buildDoc();
  const child = (id: string, title: string): MindMapNode => ({
    id,
    parentId: "root",
    title,
    notes: null,
    status: "active",
    owner: null,
    lockedBy: null,
    sourceMeetingIds: [],
    position: null,
    collapsed: false,
  });
  return { ...base, nodes: [...base.nodes, child("a", "Větev A"), child("b", "List B")] };
}

function buildDetail(overrides: Partial<MindMapDetail> = {}): MindMapDetail {
  return {
    id: "map-1",
    name: "Testovací mapa",
    description: null,
    status: "Idle",
    lastError: null,
    documentJson: JSON.stringify(buildDoc()),
    meetings: [],
    versions: [
      { versionNumber: 1, createdAt: "2026-08-01T00:00:00Z", triggerMeetingId: null, triggerMeetingSubject: null },
    ],
    ...overrides,
  };
}

function renderPanel(overrides: Partial<MindMapSidePanelProps> = {}) {
  return render(
    <MindMapSidePanel
      detail={buildDetail()}
      document={buildDoc()}
      selectedNodeId={null}
      isReadOnly={false}
      isDirty={false}
      onUpdateNode={jest.fn()}
      {...overrides}
    />,
  );
}

describe("MindMapSidePanel", () => {
  const attachMutateAsync = jest.fn();
  const detachMutateAsync = jest.fn();
  const restoreMutateAsync = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    (useAttachMeeting as jest.Mock).mockReturnValue({ mutateAsync: attachMutateAsync, isPending: false });
    (useDetachMeeting as jest.Mock).mockReturnValue({ mutateAsync: detachMutateAsync, isPending: false });
    (useRestoreMindMapVersion as jest.Mock).mockReturnValue({ mutateAsync: restoreMutateAsync, isPending: false });
    (useMeetingTasksList as jest.Mock).mockReturnValue({ data: { items: [], totalCount: 0 }, isLoading: false, error: null });
  });

  it("refuses to open the attach dialog and tells the user to save first when there are unsaved edits", () => {
    renderPanel({ isDirty: true });

    fireEvent.click(screen.getByText("Porady"));
    fireEvent.click(screen.getByTestId("mindmap-attach-button"));

    expect(toast.error).toHaveBeenCalledWith("Nejprve uložte mapu, poté můžete připojit poradu.");
    // The dialog never opened, so no option list was rendered and the mutation the
    // dialog would eventually call was never reached.
    expect(screen.queryByTestId("mindmap-attach-option")).not.toBeInTheDocument();
    expect(attachMutateAsync).not.toHaveBeenCalled();
  });

  it("opens the attach dialog normally once the map is saved (isDirty false)", () => {
    renderPanel({ isDirty: false });

    fireEvent.click(screen.getByText("Porady"));
    fireEvent.click(screen.getByTestId("mindmap-attach-button"));

    expect(toast.error).not.toHaveBeenCalled();
    // The dialog's heading uses the same label as the button that opens it
    // ("Připojit poradu"), so assert on dialog-only content instead.
    expect(screen.getByRole("heading", { name: "Připojit poradu" })).toBeInTheDocument();
  });

  it("refuses to restore a version and tells the user to save first when there are unsaved edits", () => {
    renderPanel({ isDirty: true });

    fireEvent.click(screen.getByText("Historie"));
    fireEvent.click(screen.getByRole("button", { name: "Obnovit" }));

    expect(toast.error).toHaveBeenCalledWith("Nejprve uložte mapu, poté ji můžete obnovit na starší verzi.");
    expect(restoreMutateAsync).not.toHaveBeenCalled();
  });

  it("does not push a document change on every keystroke in the title field", () => {
    const onUpdateNode = jest.fn();
    renderPanel({ document: docWithChildren(), onUpdateNode, selectedNodeId: "a" });

    const input = screen.getByTestId("mindmap-panel-title-input");
    fireEvent.change(input, { target: { value: "Nov" } });
    fireEvent.change(input, { target: { value: "Nový" } });

    // Each keystroke would otherwise trigger a full mind-elixir re-layout.
    expect(onUpdateNode).not.toHaveBeenCalled();
    expect(input).toHaveValue("Nový");
  });

  it("commits the title when the field loses focus", () => {
    const onUpdateNode = jest.fn();
    renderPanel({ document: docWithChildren(), onUpdateNode, selectedNodeId: "a" });

    const input = screen.getByTestId("mindmap-panel-title-input");
    fireEvent.change(input, { target: { value: "Nový název" } });
    fireEvent.blur(input);

    expect(onUpdateNode).toHaveBeenCalledWith("a", { title: "Nový název" });
  });

  it("does not commit when the text is unchanged", () => {
    const onUpdateNode = jest.fn();
    renderPanel({ document: docWithChildren(), onUpdateNode, selectedNodeId: "a" });
    fireEvent.blur(screen.getByTestId("mindmap-panel-title-input"));
    expect(onUpdateNode).not.toHaveBeenCalled();
  });

  it("shows each node's own values, and an abandoned draft does not leak across selections", () => {
    // The draft is reset by keying the field on the node id; without that key, typing
    // into one node and selecting another would show the first node's text.
    const { unmount } = renderPanel({ document: docWithChildren(), selectedNodeId: "a" });
    fireEvent.change(screen.getByTestId("mindmap-panel-title-input"), { target: { value: "rozepsáno" } });
    unmount();

    renderPanel({ document: docWithChildren(), selectedNodeId: "b" });
    expect(screen.getByTestId("mindmap-panel-title-input")).toHaveValue("List B");
  });

  it("still commits status immediately — a select has no intermediate states", () => {
    const onUpdateNode = jest.fn();
    renderPanel({ document: docWithChildren(), onUpdateNode, selectedNodeId: "a" });
    fireEvent.change(screen.getByLabelText("Stav"), { target: { value: "done" } });
    expect(onUpdateNode).toHaveBeenCalledWith("a", { status: "done" });
  });
});
