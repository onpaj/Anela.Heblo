import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import toast from "react-hot-toast";
import MindMapSidePanel, { MindMapSidePanelProps } from "../MindMapSidePanel";
import { MindMapDetail, useAttachMeeting, useDetachMeeting, useRestoreMindMapVersion } from "../../../../../api/hooks/useMindMaps";
import { useMeetingTasksList } from "../../../../../api/hooks/useMeetingTasks";
import { MindMapDocument } from "../mindMapDocument";

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
      onAddChild={jest.fn()}
      onDeleteNode={jest.fn()}
      onToggleCollapsed={jest.fn()}
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
});
