import React from "react";
import { act, render } from "@testing-library/react";
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
// (keeping the real `applyNodeChanges` and types via `requireActual`) so these
// tests exercise MindMapCanvas's own onNodesChange filtering and prop wiring
// without needing a browser.
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

function renderCanvas(selectedNodeId: string | null = null) {
  return render(
    <MindMapCanvas
      document={buildDoc()}
      isReadOnly={false}
      selectedNodeId={selectedNodeId}
      onSelectNode={noop}
      onNodeDragStop={noop}
      onNodeDoubleClick={noop}
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

  it("does not let a `remove` change (e.g. from a stray Backspace) desync the canvas from the document", () => {
    renderCanvas("root");
    expect(latestProps().nodes.some((n: any) => n.id === "root")).toBe(true);

    act(() => {
      latestProps().onNodesChange([{ id: "root", type: "remove" }]);
    });

    // A `remove` change is not one of the types this canvas mirrors — the node
    // must still be present after it. Node deletion has exactly one real path:
    // the side panel's "Smazat uzel", which goes through the document (and
    // marks it dirty). If this regresses, the node vanishes from the canvas
    // while staying in `localDoc`, silently reappearing on the next edit.
    expect(latestProps().nodes.some((n: any) => n.id === "root")).toBe(true);
  });
});
