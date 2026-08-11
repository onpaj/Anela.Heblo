import React from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
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
  beginEdit: jest.fn(),
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
      onOpenNodeEditor={jest.fn()}
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
    // The component replaces `instance.beginEdit` on mount and restores it (via
    // `.bind()`) on unmount. `.bind()` returns a plain function, not a tracked
    // jest mock, so across the file's many mount/unmount cycles on this one
    // shared `instance` it would otherwise nest a new bind wrapper on every test,
    // losing `.mock` identity. Give each test a fresh mock to replace onto.
    instance.beginEdit = jest.fn();
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

  it("collapseAll never calls expandNode on the root, which has no expander element", () => {
    // Regression: collapseAll used to follow the collapse with expandNode(root, true)
    // to "re-open the root". mind-elixir's expandNode writes `.expanded` onto the
    // node's <me-epd> expander, reached as el.parentNode.children[1] — and the root
    // has no expander, so every click on "Sbalit" threw
    // "Cannot set properties of undefined (setting 'expanded')" and took the page
    // down with a React error overlay.
    //
    // The stub models that precondition, so re-adding the call fails this test
    // instead of passing against an inert jest.fn().
    const rootEle = { nodeObj: { id: "root" }, parentNode: { children: [{}] } };
    instance.findEle.mockReturnValue(rootEle);
    instance.expandNode.mockImplementation((el: any, isExpand: boolean) => {
      // Deliberately mirrors mind-elixir's own traversal; Testing Library's
      // node-access rule is about querying rendered output, not about modelling a
      // third-party library's internals in a stub.
      // eslint-disable-next-line testing-library/no-node-access
      el.parentNode.children[1].expanded = isExpand;
    });

    const { ref } = renderCanvas();
    expect(() =>
      act(() => {
        ref.current!.collapseAll();
      }),
    ).not.toThrow();

    expect(instance.expandNodeAll).toHaveBeenCalledWith(rootEle, false);
    expect(instance.expandNode).not.toHaveBeenCalled();
  });

  it("collapseAll restores the root's own expanded flag so the saved document stays truthful", () => {
    // expandNodeAll flips the root's flag too. Nothing renders differently, but the
    // flag round-trips into the saved JSON as `collapsed: true` on the root.
    const rootEle = { nodeObj: { id: "root" }, parentNode: { children: [{}] } };
    instance.findEle.mockReturnValue(rootEle);
    instance.nodeData = { id: "root", expanded: true } as never;
    instance.expandNodeAll.mockImplementation(() => {
      (instance.nodeData as { expanded: boolean }).expanded = false;
    });

    const { ref } = renderCanvas();
    act(() => {
      ref.current!.collapseAll();
    });

    expect((instance.nodeData as { expanded: boolean }).expanded).toBe(true);
  });

  it("copies each branch's inline colour onto its me-main as --branch-color", () => {
    // mind-elixir sets the branch colour as an inline border-color on the branch's
    // own <me-tpc>; deeper cards need it as an inheritable variable to tint their
    // borders. `linkDiv` fires after every layout pass.
    renderCanvas();
    const container = screen.getByTestId("mindmap-canvas");
    container.innerHTML =
      "<me-main><me-wrapper><me-parent>" +
      '<me-tpc style="border-color: rgb(46, 125, 107)"></me-tpc>' +
      "</me-parent><me-children><me-wrapper><me-parent><me-tpc></me-tpc>" +
      "</me-parent></me-wrapper></me-children></me-wrapper></me-main>";

    act(() => {
      instance.bus.fire("linkDiv");
    });

    // mind-elixir owns this subtree and renders custom elements; Testing Library
    // queries cannot address <me-main>, so query it directly.
    // eslint-disable-next-line testing-library/no-node-access
    const branch = container.querySelector("me-main") as HTMLElement;
    expect(branch.style.getPropertyValue("--branch-color")).toBe("rgb(46, 125, 107)");
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

  it("ignores F2 while mind-elixir's own inline editor is already open, so a repeat doesn't stack a second input box", () => {
    // Captured before the component replaces the method on mount.
    const inlineEditor = instance.beginEdit as jest.Mock;
    renderCanvas();

    const canvas = screen.getByTestId("mindmap-canvas");
    const inputBox = window.document.createElement("div");
    inputBox.id = "input-box";
    canvas.appendChild(inputBox);

    fireEvent.keyDown(inputBox, { key: "F2" });

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
});
