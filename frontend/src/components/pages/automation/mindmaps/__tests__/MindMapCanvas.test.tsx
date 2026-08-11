import React from "react";
import { act, render } from "@testing-library/react";
import "@testing-library/jest-dom";
import { MindMapDocument } from "../mindMapDocument";

jest.mock("mind-elixir/style", () => ({}), { virtual: true });

// mind-elixir drives real DOM layout (offsetWidth, getBoundingClientRect), all of
// which jsdom reports as 0 — a real instance renders an empty, meaningless map.
// Stub the class so these tests exercise MindMapCanvas's own wiring: mount once,
// refresh on revision change, toggle edit mode, translate bus events outward.
const instance = {
  init: jest.fn(),
  destroy: jest.fn(),
  refresh: jest.fn(),
  clearHistory: jest.fn(),
  getData: jest.fn(),
  enableEdit: jest.fn(),
  disableEdit: jest.fn(),
  undo: jest.fn(),
  scaleFit: jest.fn(),
  toCenter: jest.fn(),
  addChild: jest.fn(),
  insertSibling: jest.fn(),
  removeNodes: jest.fn(),
  reshapeNode: jest.fn(),
  expandNode: jest.fn(),
  expandNodeAll: jest.fn(),
  findEle: jest.fn(() => ({ nodeObj: { id: "root" } })),
  changeTheme: jest.fn(),
  exportPng: jest.fn(),
  exportSvg: jest.fn(),
  nodeData: { id: "root" },
  currentNode: null as unknown,
  bus: {
    listeners: {} as Record<string, Function[]>,
    addListener(type: string, handler: Function) {
      (this.listeners[type] ??= []).push(handler);
    },
    removeListener(type: string, handler: Function) {
      this.listeners[type] = (this.listeners[type] ?? []).filter((h) => h !== handler);
    },
    fire(type: string, ...args: unknown[]) {
      (this.listeners[type] ?? []).forEach((h) => h(...args));
    },
  },
};

const mockMindElixir: any = jest.fn(() => instance);
mockMindElixir.SIDE = 2;
mockMindElixir.LEFT = 0;
mockMindElixir.RIGHT = 1;

jest.mock("mind-elixir", () => ({ __esModule: true, default: mockMindElixir }));

// `import MindMapCanvas from "../MindMapCanvas"` would be an ES import, and ES
// imports are hoisted above plain `const` statements by Babel's ESM→CJS
// transform — even below the `// eslint-disable-next-line import/first` comment,
// which only silences the lint rule and has no effect on runtime hoisting. That
// hoisting would require "../MindMapCanvas" (and transitively "mind-elixir",
// triggering the jest.mock factory below) before `mockMindElixir` is initialised,
// producing a TDZ ReferenceError. `require()` is not hoisted, so it runs in the
// order written, after `mockMindElixir` exists.
// eslint-disable-next-line import/first
import type MindMapCanvasType from "../MindMapCanvas";
// eslint-disable-next-line import/first
import type { MindMapCanvasHandle } from "../MindMapCanvas";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const MindMapCanvas: typeof MindMapCanvasType = require("../MindMapCanvas").default;

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

function renderCanvas(overrides: Partial<React.ComponentProps<typeof MindMapCanvas>> = {}) {
  const ref = React.createRef<MindMapCanvasHandle>();
  const utils = render(
    <MindMapCanvas
      ref={ref}
      initialDocument={buildDoc()}
      documentRevision="rev-1"
      isReadOnly={false}
      onChange={jest.fn()}
      onSelectNode={jest.fn()}
      {...overrides}
    />,
  );
  return { ref, ...utils };
}

describe("MindMapCanvas", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    instance.bus.listeners = {};
    // CRA's Jest config sets `resetMocks: true` (config/jest global default), which
    // runs before every test and strips the `() => instance` implementation given
    // to `mockMindElixir` at module load — without this, `new MindElixir(...)`
    // would return a bare auto-generated object instead of our shared `instance`.
    mockMindElixir.mockImplementation(() => instance);
  });

  it("creates the instance with the two-sided layout and initialises it once", () => {
    renderCanvas();
    expect(mockMindElixir).toHaveBeenCalledTimes(1);
    expect(mockMindElixir.mock.calls[0][0]).toEqual(
      expect.objectContaining({ direction: 2, allowUndo: true }),
    );
    expect(instance.init).toHaveBeenCalledTimes(1);
  });

  it("keeps mind-elixir's own context menu and toolbar off", () => {
    // The context menu can create arrows and summaries, neither of which
    // MindMapDocument stores — they would be silently dropped on the next save —
    // and mind-elixir has no Czech language pack for it.
    renderCanvas();
    expect(mockMindElixir.mock.calls[0][0]).toEqual(
      expect.objectContaining({ contextMenu: false, toolBar: false }),
    );
  });

  it("does not re-initialise when the revision is unchanged", () => {
    const { rerender, ref } = renderCanvas();
    rerender(
      <MindMapCanvas
        ref={ref}
        initialDocument={buildDoc()}
        documentRevision="rev-1"
        isReadOnly={false}
        onChange={jest.fn()}
        onSelectNode={jest.fn()}
      />,
    );
    expect(instance.refresh).not.toHaveBeenCalled();
  });

  it("refreshes and clears history when a new server revision arrives", () => {
    // clearHistory matters: without it ⌘Z can undo backwards into the previous
    // document, producing node ids the server has already replaced.
    const { rerender, ref } = renderCanvas();
    rerender(
      <MindMapCanvas
        ref={ref}
        initialDocument={buildDoc()}
        documentRevision="rev-2"
        isReadOnly={false}
        onChange={jest.fn()}
        onSelectNode={jest.fn()}
      />,
    );
    expect(instance.refresh).toHaveBeenCalledTimes(1);
    expect(instance.clearHistory).toHaveBeenCalledTimes(1);
  });

  it("reports edits upward so the page can mark the document dirty", () => {
    const onChange = jest.fn();
    renderCanvas({ onChange });
    act(() => {
      instance.bus.fire("operation", { name: "addChild", obj: { id: "x" } });
    });
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("treats collapsing a branch as an edit — `collapsed` is a persisted field", () => {
    const onChange = jest.fn();
    renderCanvas({ onChange });
    act(() => {
      instance.bus.fire("expandNode", { id: "x" });
    });
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("reports selection and deselection upward", () => {
    const onSelectNode = jest.fn();
    renderCanvas({ onSelectNode });
    act(() => {
      instance.bus.fire("selectNewNode", { id: "a" });
    });
    expect(onSelectNode).toHaveBeenCalledWith("a");
    act(() => {
      instance.bus.fire("unselectNodes", [{ id: "a" }]);
    });
    expect(onSelectNode).toHaveBeenCalledWith(null);
  });

  it("reports the node id upward when a plain click selects exactly one node (the real selection path)", () => {
    // A plain user click fires `selectNodes` with a one-element array, not
    // `selectNewNode` — see the handleSelectNodes comment in MindMapCanvas.tsx.
    const onSelectNode = jest.fn();
    renderCanvas({ onSelectNode });
    act(() => {
      instance.bus.fire("selectNodes", [{ id: "a" }]);
    });
    expect(onSelectNode).toHaveBeenCalledWith("a");
  });

  it("reports nothing when selectNodes fires with more than one node (multi-select must not produce a bogus single selection)", () => {
    const onSelectNode = jest.fn();
    renderCanvas({ onSelectNode });
    act(() => {
      instance.bus.fire("selectNodes", [{ id: "a" }, { id: "b" }]);
    });
    expect(onSelectNode).not.toHaveBeenCalled();
  });

  it("disables editing while the map is read-only and re-enables it after", () => {
    const { rerender, ref } = renderCanvas({ isReadOnly: true });
    expect(instance.disableEdit).toHaveBeenCalled();
    rerender(
      <MindMapCanvas
        ref={ref}
        initialDocument={buildDoc()}
        documentRevision="rev-1"
        isReadOnly={false}
        onChange={jest.fn()}
        onSelectNode={jest.fn()}
      />,
    );
    expect(instance.enableEdit).toHaveBeenCalled();
  });

  it("exposes the current document through the ref handle", () => {
    instance.getData.mockReturnValue({ nodeData: { id: "root", topic: "Projekt" } });
    const { ref } = renderCanvas();
    expect(ref.current!.getDocument()).toEqual(
      expect.objectContaining({ rootNodeId: "root", schemaVersion: 1 }),
    );
  });

  it("routes side-panel field edits through reshapeNode, preserving untouched metadata", () => {
    instance.currentNode = null;
    instance.findEle.mockReturnValue({
      nodeObj: {
        id: "root",
        topic: "Projekt",
        metadata: { status: "active", owner: "Bára", lockedBy: null, sourceMeetingIds: ["m1"] },
      },
    });
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.patchNode("root", { status: "done" });
    });
    expect(instance.reshapeNode).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        metadata: { status: "done", owner: "Bára", lockedBy: null, sourceMeetingIds: ["m1"] },
      }),
    );
  });

  it("expandAll expands every node starting from the root", () => {
    // resetMocks (CRA's Jest default) strips findEle's default implementation
    // before every test, same as the beforeEach hook has to restore it for
    // mockMindElixir — give it one here too.
    const rootEle = { nodeObj: { id: "root" } };
    instance.findEle.mockReturnValue(rootEle);
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.expandAll();
    });
    expect(instance.findEle).toHaveBeenCalledWith(instance.nodeData.id);
    expect(instance.expandNodeAll).toHaveBeenCalledWith(rootEle, true);
  });

  it("collapseAll collapses everything, then re-expands the root so the map never disappears", () => {
    const rootEle = { nodeObj: { id: "root" } };
    instance.findEle.mockReturnValue(rootEle);
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.collapseAll();
    });
    expect(instance.expandNodeAll).toHaveBeenCalledWith(rootEle, false);
    expect(instance.expandNode).toHaveBeenCalledWith(rootEle, true);
    // Order matters: re-expanding the root before the collapse would just have it
    // collapsed again by expandNodeAll.
    expect(instance.expandNodeAll.mock.invocationCallOrder[0]).toBeLessThan(
      instance.expandNode.mock.invocationCallOrder[0],
    );
  });

  it("fit re-centers before rescaling to fit", () => {
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.fit();
    });
    expect(instance.toCenter).toHaveBeenCalledTimes(1);
    expect(instance.scaleFit).toHaveBeenCalledTimes(1);
    expect(instance.toCenter.mock.invocationCallOrder[0]).toBeLessThan(
      instance.scaleFit.mock.invocationCallOrder[0],
    );
  });

  it("addChild adds a child under the currently selected node", () => {
    instance.currentNode = { id: "root" };
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.addChild();
    });
    expect(instance.addChild).toHaveBeenCalledWith(instance.currentNode);
  });

  it("addChild does nothing when nothing is selected", () => {
    instance.currentNode = null;
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.addChild();
    });
    expect(instance.addChild).not.toHaveBeenCalled();
  });

  it("addSibling inserts a sibling after the currently selected node", () => {
    instance.currentNode = { id: "a" };
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.addSibling();
    });
    expect(instance.insertSibling).toHaveBeenCalledWith("after", instance.currentNode);
  });

  it("addSibling does nothing when nothing is selected", () => {
    instance.currentNode = null;
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.addSibling();
    });
    expect(instance.insertSibling).not.toHaveBeenCalled();
  });

  it("exportPng resolves whatever blob mind-elixir produces", async () => {
    const blob = new Blob(["png"]);
    instance.exportPng.mockResolvedValue(blob);
    const { ref } = renderCanvas();
    await expect(ref.current!.exportPng()).resolves.toBe(blob);
  });

  it("exportSvg returns whatever blob mind-elixir produces", () => {
    const blob = new Blob(["svg"]);
    instance.exportSvg.mockReturnValue(blob);
    const { ref } = renderCanvas();
    expect(ref.current!.exportSvg()).toBe(blob);
  });

  it("destroys the instance on unmount", () => {
    const { unmount } = renderCanvas();
    unmount();
    expect(instance.destroy).toHaveBeenCalledTimes(1);
  });
});
