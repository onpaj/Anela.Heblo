import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useRef } from "react";
import MindElixir from "mind-elixir";
import type { MindElixirInstance, NodeObj, Topic } from "mind-elixir";
import "mind-elixir/style";
import "./mindMapCanvas.css";
import { useTheme } from "../../../../contexts/ThemeContext";
import { MindMapDocument, MindMapNodePatch } from "./mindMapDocument";
import {
  DEFAULT_METADATA,
  displayFieldsFor,
  fromMindElixir,
  MindMapNodeMetadata,
  MindMapNodeObj,
  toMindElixir,
} from "./mindElixirMapping";
import { themeFor } from "./mindElixirTheme";

export type { MindMapNodePatch };

export interface MindMapCanvasHandle {
  getDocument: () => MindMapDocument | null;
  expandAll: () => void;
  collapseAll: () => void;
  fit: () => void;
  addChild: () => void;
  addSibling: () => void;
  undo: () => void;
  patchNode: (nodeId: string, patch: MindMapNodePatch) => void;
  exportPng: () => Promise<Blob | null>;
  exportSvg: () => Blob | null;
}

export interface MindMapCanvasProps {
  /** Used once, on mount. Later server documents arrive via `documentRevision`. */
  initialDocument: MindMapDocument;
  /**
   * Opaque token identifying the server document currently loaded. Changing it
   * reloads the map; keeping it stable leaves the user's in-progress edits alone.
   * The page passes the raw `documentJson` string.
   */
  documentRevision: string;
  isReadOnly: boolean;
  /** Any edit the user made — the page turns this into `isDirty`. */
  onChange: () => void;
  onSelectNode: (nodeId: string | null) => void;
  /** A double-click (or F2's replacement) asks the page to open the node editor. */
  onOpenNodeEditor: (nodeId: string) => void;
}

const MindMapCanvas = forwardRef<MindMapCanvasHandle, MindMapCanvasProps>(function MindMapCanvas(
  { initialDocument, documentRevision, isReadOnly, onChange, onSelectNode, onOpenNodeEditor },
  ref,
) {
  const { theme } = useTheme();
  const containerRef = useRef<HTMLDivElement>(null);
  const instanceRef = useRef<MindElixirInstance | null>(null);
  const loadedRevisionRef = useRef<string>(documentRevision);
  // The document the editor was loaded with — supplies schemaVersion and the
  // tombstone list, neither of which mind-elixir knows anything about.
  const baseDocumentRef = useRef<MindMapDocument>(initialDocument);

  // Latest callbacks, so the mount effect can stay dependency-free and the
  // instance is never torn down just because the page re-rendered.
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

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return undefined;

    const instance = new MindElixir({
      el: container,
      direction: MindElixir.SIDE,
      allowUndo: true,
      // Off for two independent reasons, both load-bearing:
      //  1. Its menu creates arrows ("link") and summaries. MindMapDocument stores
      //     neither, and fromMindElixir reads only `nodeData` — anything a user
      //     created that way would be silently discarded on the next save. The
      //     ContextMenuOption type can disable `link` but NOT `summary`, so there
      //     is no partial setting that closes the hole.
      //  2. mind-elixir ships no Czech language pack (cn/en/ru/ja/pt/it/es/fr/ko/
      //     ro/da/fi/de/nl only), and this UI is Czech throughout.
      // Our toolbar covers add-sibling/add-child/undo, ⌫ deletes, and ⌘↑/⌘↓ reorder.
      contextMenu: false,
      toolBar: false, // we render our own Czech toolbar
      keypress: true,
      theme: themeFor(theme === "dark" ? "dark" : "light"),
    });
    instance.init(toMindElixir(baseDocumentRef.current));
    instanceRef.current = instance;

    // mind-elixir has no DOM `dblclick` event to intercept: it detects double taps
    // itself inside its pointerup handler and calls instance.beginEdit (verified in
    // dist/MindElixir.js — the double-tap branch bails at `if (!e.editable) return`
    // and then does `selectNode(b), beginEdit(b)`). There is no option that turns
    // inline editing off, so replacing the method is the only seam: it swaps the
    // library's inline #input-box for our own editor dialog.
    //
    // Keep the raw, unbound reference too: `beginEdit` is a prototype method, so
    // calling it as `instance.beginEdit(...)` (the library's own call style, and
    // the only way it is ever invoked here again after cleanup) already gets the
    // right `this` from the method-call syntax — a `.bind()` copy is only needed
    // for `handleF2` below, which calls it detached from `instance.`. Restoring
    // the bound copy instead of the original would still behave correctly, but it
    // would permanently shadow the library's own method with an extra wrapper.
    const originalBeginEdit = instance.beginEdit;
    const inlineBeginEdit = originalBeginEdit.bind(instance);
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

    const handleEdit = () => onChangeRef.current();
    // A plain click does NOT fire `selectNewNode` — verified against mind-elixir
    // 5.15.1's own source (dist/MindElixir.js). `selectNewNode` is fired only when
    // selectNode() is called with its `dispatch` flag (addChild/insertSibling
    // auto-selecting the node they just created). An ordinary user click goes
    // through the bundled box-selection library's select()/deselect(), which fires
    // `selectNodes`/`unselectNodes` with an ARRAY of nodeObj — one element for a
    // single click. Both listeners are kept: `selectNewNode` for the toolbar's
    // addChild/addSibling auto-select, `selectNodes` for real clicks.
    const handleSelect = (node: { id: string }) => onSelectNodeRef.current(node.id);
    const handleSelectNodes = (nodes: NodeObj[]) => {
      if (nodes.length === 1) onSelectNodeRef.current(nodes[0].id);
    };
    const handleUnselect = () => onSelectNodeRef.current(null);

    // mind-elixir colours each top-level branch from the theme palette, but it
    // applies that colour as an INLINE border-color on the branch's own <me-tpc>.
    // It is not a CSS variable and it is not set on any ancestor, so deeper cards
    // have no way to reach it in CSS. Copy it onto the branch's <me-main> as
    // --branch-color, which mindMapCanvas.css then uses to tint every card in that
    // branch. `linkDiv` fires after every layout pass, which is exactly when the
    // elements have been rebuilt.
    const paintBranchColors = () => {
      const container = containerRef.current;
      if (!container) return;
      container.querySelectorAll("me-main").forEach((branch) => {
        const topic = branch.querySelector<HTMLElement>(":scope > me-wrapper > me-parent > me-tpc");
        const color = topic?.style.borderColor;
        if (color) (branch as HTMLElement).style.setProperty("--branch-color", color);
      });
    };

    instance.bus.addListener("linkDiv", paintBranchColors);
    paintBranchColors();

    instance.bus.addListener("operation", handleEdit);
    // Collapsing a branch is a persisted change (`collapsed`), but it is NOT an
    // `operation` — it has its own event.
    instance.bus.addListener("expandNode", handleEdit);
    instance.bus.addListener("selectNewNode", handleSelect);
    instance.bus.addListener("selectNodes", handleSelectNodes);
    instance.bus.addListener("unselectNodes", handleUnselect);

    return () => {
      instance.bus.removeListener("linkDiv", paintBranchColors);
      instance.bus.removeListener("operation", handleEdit);
      instance.bus.removeListener("expandNode", handleEdit);
      instance.bus.removeListener("selectNewNode", handleSelect);
      instance.bus.removeListener("selectNodes", handleSelectNodes);
      instance.bus.removeListener("unselectNodes", handleUnselect);
      window.document.removeEventListener("keydown", handleF2, true);
      container.removeEventListener("dblclick", handleDoubleClick);
      instance.beginEdit = originalBeginEdit;
      instance.destroy();
      instanceRef.current = null;
    };
    // Mount once. Data, theme and read-only state are pushed by the effects below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Adopt a new server document. The page only bumps the revision when it is safe
  // to do so (no unsaved edits), so this never clobbers work in progress.
  useEffect(() => {
    const instance = instanceRef.current;
    if (!instance || documentRevision === loadedRevisionRef.current) return;
    loadedRevisionRef.current = documentRevision;
    baseDocumentRef.current = initialDocument;
    instance.refresh(toMindElixir(initialDocument));
    instance.clearHistory?.();
  }, [documentRevision, initialDocument]);

  useEffect(() => {
    const instance = instanceRef.current;
    if (!instance) return;
    if (isReadOnly) instance.disableEdit();
    else instance.enableEdit();
  }, [isReadOnly]);

  useEffect(() => {
    const instance = instanceRef.current;
    if (!instance) return;
    instance.changeTheme(themeFor(theme === "dark" ? "dark" : "light"), true);
  }, [theme]);

  const currentTopic = useCallback(() => {
    const instance = instanceRef.current;
    return instance?.currentNode ?? null;
  }, []);

  useImperativeHandle(
    ref,
    (): MindMapCanvasHandle => ({
      getDocument: () => {
        const instance = instanceRef.current;
        if (!instance) return null;
        return fromMindElixir(instance.getData(), baseDocumentRef.current);
      },
      expandAll: () => {
        const instance = instanceRef.current;
        if (instance) instance.expandNodeAll(instance.findEle(instance.nodeData.id), true);
      },
      collapseAll: () => {
        const instance = instanceRef.current;
        if (!instance) return;
        // `expandNodeAll(root, false)` already leaves the root's own children on
        // screen, which IS "collapse every branch but keep the root open" — it
        // renders the top level regardless of the root's own `expanded` flag.
        //
        // Do NOT follow it with `expandNode(root, true)`. expandNode reaches for
        // the node's expander element (`el.parentNode.children[1]`) and writes
        // `.expanded` on it; the ROOT has no expander (verified: `me-root`'s
        // parent has a single child), so that call always threw
        // "Cannot set properties of undefined (setting 'expanded')" and took the
        // whole page down with a React error overlay.
        instance.expandNodeAll(instance.findEle(instance.nodeData.id), false);
        // expandNodeAll flips the root's own flag too. Nothing renders differently
        // because of it, but a document claiming its root is collapsed is wrong and
        // would round-trip into the saved JSON, so put it back.
        instance.nodeData.expanded = true;
      },
      fit: () => {
        instanceRef.current?.toCenter();
        instanceRef.current?.scaleFit();
      },
      addChild: () => {
        const topic = currentTopic();
        if (topic) void instanceRef.current?.addChild(topic);
      },
      addSibling: () => {
        const topic = currentTopic();
        if (topic) void instanceRef.current?.insertSibling("after", topic);
      },
      undo: () => instanceRef.current?.undo(),
      patchNode: (nodeId, patch) => {
        const instance = instanceRef.current;
        if (!instance) return;
        const topic = instance.findEle(nodeId);
        if (!topic) return;
        const nodeObj = topic.nodeObj as MindMapNodeObj;
        const previous: MindMapNodeMetadata = nodeObj.metadata ?? DEFAULT_METADATA;
        // reshapeNode replaces `metadata` wholesale, so merge before writing —
        // otherwise editing the owner would silently drop lockedBy and provenance.
        const metadata: MindMapNodeMetadata = {
          status: patch.status ?? previous.status,
          owner: patch.owner !== undefined ? patch.owner : previous.owner,
          lockedBy: previous.lockedBy,
          sourceMeetingIds: previous.sourceMeetingIds,
        };
        const notes = patch.notes !== undefined ? patch.notes : nodeObj.note ?? null;
        void instance.reshapeNode(topic, {
          ...(patch.title !== undefined ? { topic: patch.title } : {}),
          note: notes ?? undefined,
          metadata,
          ...displayFieldsFor(metadata, notes),
        });
      },
      exportPng: async () => (await instanceRef.current?.exportPng()) ?? null,
      exportSvg: () => instanceRef.current?.exportSvg() ?? null,
    }),
    [currentTopic],
  );

  return (
    <div
      data-testid="mindmap-canvas"
      ref={containerRef}
      className="mindmap-canvas h-full w-full"
    />
  );
});

export default MindMapCanvas;
