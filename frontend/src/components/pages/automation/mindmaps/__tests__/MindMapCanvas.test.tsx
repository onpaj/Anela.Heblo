import React from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { MindMapDocument } from "../mindMapDocument";

// This repo's package.json overrides Jest's transformIgnorePatterns to skip all of
// node_modules except date-fns, so @xyflow/react's own CSS import (pulled in by
// MindMapCanvas.tsx) fails to parse as JS. No other test imports the real
// MindMapCanvas module (MindMapDetailPage.test.tsx mocks it out), so this is the
// first to hit it — stub the stylesheet import itself rather than touching the
// repo's shared Jest config.
jest.mock("@xyflow/react/dist/style.css", () => ({}));

// Real React Flow needs browser APIs (ResizeObserver etc.) jsdom doesn't provide,
// and no test in this repo renders it. Stub only the visual `ReactFlow` component
// (keeping the real types via `requireActual`) so these tests exercise
// MindMapCanvas's own prop wiring without needing a browser.
const mockReactFlow = jest.fn((props: any) => (
  <div data-testid="reactflow-stub">
    {props.nodes.map((n: any) => (
      <div key={n.id} data-testid={`node-${n.id}`} />
    ))}
  </div>
));

jest.mock("@xyflow/react", () => {
  const actual = jest.requireActual("@xyflow/react");
  return {
    ...actual,
    ReactFlow: (props: any) => mockReactFlow(props),
    Background: () => null,
    Controls: () => null,
  };
});

// eslint-disable-next-line import/first
import MindMapCanvas from "../MindMapCanvas";

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

function latestProps() {
  return mockReactFlow.mock.calls[mockReactFlow.mock.calls.length - 1][0];
}

const noop = () => {};

function renderCanvas(overrides: Partial<React.ComponentProps<typeof MindMapCanvas>> = {}) {
  return render(
    <MindMapCanvas
      document={buildDoc()}
      isReadOnly={false}
      selectedNodeId={null}
      editingNodeId={null}
      onSelectNode={noop}
      onNodeDoubleClick={noop}
      onCommitEdit={noop}
      onCancelEdit={noop}
      onCommitAndAddSibling={noop}
      onToggleCollapsed={noop}
      onKeyDown={noop}
      {...overrides}
    />,
  );
}

describe("MindMapCanvas", () => {
  beforeEach(() => {
    mockReactFlow.mockClear();
  });

  it("wires deleteKeyCode off so React Flow's default Backspace-deletes-selected-node shortcut is inert", () => {
    renderCanvas();
    expect(latestProps().deleteKeyCode).toBeNull();
  });

  it("keeps nodes undraggable — the two-sided layout owns every position", () => {
    renderCanvas();
    expect(latestProps().nodesDraggable).toBe(false);
  });

  it("marks only the selected node as selected", () => {
    renderCanvas({ selectedNodeId: "root" });
    expect(latestProps().nodes.find((n: any) => n.id === "root").selected).toBe(true);
  });

  it("hands the page a fitView callback once React Flow initialises", () => {
    const fitView = jest.fn();
    const onFitViewReady = jest.fn();
    renderCanvas({ onFitViewReady });

    act(() => {
      latestProps().onInit({ fitView } as any);
    });

    expect(onFitViewReady).toHaveBeenCalledTimes(1);
    onFitViewReady.mock.calls[0][0]();
    expect(fitView).toHaveBeenCalled();
  });

  it("scopes shortcut handling to the canvas element rather than the document", () => {
    const onKeyDown = jest.fn();
    renderCanvas({ onKeyDown });
    const canvas = screen.getByTestId("mindmap-canvas");

    // Focusable, so Enter/Tab/Space are only intercepted while the map has focus —
    // typing in the side panel's inputs must never reach these shortcuts.
    expect(canvas).toHaveAttribute("tabindex", "0");
    fireEvent.keyDown(canvas, { key: "Enter" });
    expect(onKeyDown).toHaveBeenCalled();
  });
});
