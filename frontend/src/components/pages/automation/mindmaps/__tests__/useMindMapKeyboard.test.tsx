import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { MindMapDocument, MindMapNode } from "../mindMapDocument";
import { MindMapKeyboardHandlers, useMindMapKeyboard } from "../useMindMapKeyboard";

function node(id: string, parentId: string | null, overrides: Partial<MindMapNode> = {}): MindMapNode {
  return {
    id,
    parentId,
    title: id,
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

// root has 4 branches: a, b grow right; c, d grow left.
const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    node("root", null),
    node("a", "root"),
    node("b", "root"),
    node("c", "root"),
    node("d", "root"),
    node("a1", "a"),
    node("a2", "a"),
    node("c1", "c"),
  ],
  suppressedNodes: [],
});

function makeHandlers(): jest.Mocked<MindMapKeyboardHandlers> {
  return {
    onSelect: jest.fn(),
    onStartEdit: jest.fn(),
    onAddSibling: jest.fn(),
    onAddChild: jest.fn(),
    onDelete: jest.fn(),
    onToggleCollapsed: jest.fn(),
    onIndent: jest.fn(),
    onOutdent: jest.fn(),
    onMove: jest.fn(),
    onUndo: jest.fn(),
  };
}

interface HarnessProps {
  document: MindMapDocument | null;
  selectedNodeId: string | null;
  isEnabled?: boolean;
  handlers: MindMapKeyboardHandlers;
}

const Harness: React.FC<HarnessProps> = ({ document: doc_, selectedNodeId, isEnabled = true, handlers }) => {
  const onKeyDown = useMindMapKeyboard({ document: doc_, selectedNodeId, isEnabled, handlers });
  return (
    <div data-testid="canvas" tabIndex={0} onKeyDown={onKeyDown}>
      <input data-testid="field" />
    </div>
  );
};

function setup(selectedNodeId: string | null, options: Partial<HarnessProps> = {}) {
  const handlers = makeHandlers();
  render(<Harness document={doc()} selectedNodeId={selectedNodeId} handlers={handlers} {...options} />);
  return { handlers, canvas: screen.getByTestId("canvas"), field: screen.getByTestId("field") };
}

describe("useMindMapKeyboard", () => {
  it("Enter adds a sibling and Tab adds a child of the selected node", () => {
    const { handlers, canvas } = setup("a");
    fireEvent.keyDown(canvas, { key: "Enter" });
    fireEvent.keyDown(canvas, { key: "Tab" });
    expect(handlers.onAddSibling).toHaveBeenCalledWith("a");
    expect(handlers.onAddChild).toHaveBeenCalledWith("a");
  });

  it("Space toggles collapse, Backspace deletes and F2 starts editing", () => {
    const { handlers, canvas } = setup("a");
    fireEvent.keyDown(canvas, { key: " " });
    fireEvent.keyDown(canvas, { key: "Backspace" });
    fireEvent.keyDown(canvas, { key: "F2" });
    expect(handlers.onToggleCollapsed).toHaveBeenCalledWith("a");
    expect(handlers.onDelete).toHaveBeenCalledWith("a");
    expect(handlers.onStartEdit).toHaveBeenCalledWith("a");
  });

  it("walks between siblings with the up/down arrows", () => {
    const { handlers, canvas } = setup("a1");
    fireEvent.keyDown(canvas, { key: "ArrowDown" });
    expect(handlers.onSelect).toHaveBeenCalledWith("a2");
  });

  it("stops at the ends of a sibling list instead of wrapping", () => {
    const { handlers, canvas } = setup("a1");
    fireEvent.keyDown(canvas, { key: "ArrowUp" });
    expect(handlers.onSelect).not.toHaveBeenCalled();
  });

  it("on a right-side branch, ArrowRight goes deeper and ArrowLeft goes to the parent", () => {
    const { handlers, canvas } = setup("a");
    fireEvent.keyDown(canvas, { key: "ArrowRight" });
    expect(handlers.onSelect).toHaveBeenCalledWith("a1");
    fireEvent.keyDown(canvas, { key: "ArrowLeft" });
    expect(handlers.onSelect).toHaveBeenCalledWith("root");
  });

  it("mirrors the in/out arrows on a left-side branch", () => {
    const { handlers, canvas } = setup("c");
    // "c" sits left of the root, so going deeper means pressing ArrowLeft.
    fireEvent.keyDown(canvas, { key: "ArrowLeft" });
    expect(handlers.onSelect).toHaveBeenCalledWith("c1");
    fireEvent.keyDown(canvas, { key: "ArrowRight" });
    expect(handlers.onSelect).toHaveBeenCalledWith("root");
  });

  it("does not descend into a collapsed node", () => {
    const collapsed = doc();
    collapsed.nodes = collapsed.nodes.map((n) => (n.id === "a" ? { ...n, collapsed: true } : n));
    const { handlers, canvas } = setup("a", { document: collapsed });
    fireEvent.keyDown(canvas, { key: "ArrowRight" });
    expect(handlers.onSelect).not.toHaveBeenCalled();
  });

  it("selects the root when an arrow is pressed with nothing selected", () => {
    const { handlers, canvas } = setup(null);
    fireEvent.keyDown(canvas, { key: "ArrowDown" });
    expect(handlers.onSelect).toHaveBeenCalledWith("root");
  });

  it("maps the ⌘ arrows to indent, outdent and sibling reordering", () => {
    const { handlers, canvas } = setup("a2");
    fireEvent.keyDown(canvas, { key: "ArrowRight", metaKey: true });
    fireEvent.keyDown(canvas, { key: "ArrowLeft", metaKey: true });
    fireEvent.keyDown(canvas, { key: "ArrowUp", metaKey: true });
    fireEvent.keyDown(canvas, { key: "ArrowDown", metaKey: true });
    expect(handlers.onIndent).toHaveBeenCalledWith("a2");
    expect(handlers.onOutdent).toHaveBeenCalledWith("a2");
    expect(handlers.onMove).toHaveBeenCalledWith("a2", -1);
    expect(handlers.onMove).toHaveBeenCalledWith("a2", 1);
  });

  it("⌘Z undoes even with nothing selected", () => {
    const { handlers, canvas } = setup(null);
    fireEvent.keyDown(canvas, { key: "z", metaKey: true });
    expect(handlers.onUndo).toHaveBeenCalled();
  });

  it("ignores keys typed into a form field inside the canvas", () => {
    const { handlers, field } = setup("a");
    // Without this guard, typing a space into the side panel's owner field would
    // collapse a branch and Backspace would delete the selected node.
    fireEvent.keyDown(field, { key: " " });
    fireEvent.keyDown(field, { key: "Backspace" });
    fireEvent.keyDown(field, { key: "Enter" });
    expect(handlers.onToggleCollapsed).not.toHaveBeenCalled();
    expect(handlers.onDelete).not.toHaveBeenCalled();
    expect(handlers.onAddSibling).not.toHaveBeenCalled();
  });

  it("does nothing while disabled (read-only map, inline editing, open help sheet)", () => {
    const { handlers, canvas } = setup("a", { isEnabled: false });
    fireEvent.keyDown(canvas, { key: "Enter" });
    fireEvent.keyDown(canvas, { key: "Backspace" });
    expect(handlers.onAddSibling).not.toHaveBeenCalled();
    expect(handlers.onDelete).not.toHaveBeenCalled();
  });
});
