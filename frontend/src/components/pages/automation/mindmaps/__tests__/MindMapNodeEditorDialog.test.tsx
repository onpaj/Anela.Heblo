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
