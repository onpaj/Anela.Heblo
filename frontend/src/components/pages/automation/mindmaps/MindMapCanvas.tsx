import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useRef } from "react";
import MindElixir from "mind-elixir";
import type { MindElixirInstance, NodeObj } from "mind-elixir";
import "mind-elixir/style";
import "./mindMapCanvas.css";
import { useTheme } from "../../../../contexts/ThemeContext";
import { MindMapDocument, MindMapNode } from "./mindMapDocument";
import {
  DEFAULT_METADATA,
  displayFieldsFor,
  fromMindElixir,
  MindMapNodeMetadata,
  MindMapNodeObj,
  toMindElixir,
} from "./mindElixirMapping";
import { themeFor } from "./mindElixirTheme";

export type MindMapNodePatch = Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>;

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
}

const MindMapCanvas = forwardRef<MindMapCanvasHandle, MindMapCanvasProps>(function MindMapCanvas(
  { initialDocument, documentRevision, isReadOnly, onChange, onSelectNode },
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
  useEffect(() => {
    onChangeRef.current = onChange;
    onSelectNodeRef.current = onSelectNode;
  }, [onChange, onSelectNode]);

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

    instance.bus.addListener("operation", handleEdit);
    // Collapsing a branch is a persisted change (`collapsed`), but it is NOT an
    // `operation` — it has its own event.
    instance.bus.addListener("expandNode", handleEdit);
    instance.bus.addListener("selectNewNode", handleSelect);
    instance.bus.addListener("selectNodes", handleSelectNodes);
    instance.bus.addListener("unselectNodes", handleUnselect);

    return () => {
      instance.bus.removeListener("operation", handleEdit);
      instance.bus.removeListener("expandNode", handleEdit);
      instance.bus.removeListener("selectNewNode", handleSelect);
      instance.bus.removeListener("selectNodes", handleSelectNodes);
      instance.bus.removeListener("unselectNodes", handleUnselect);
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
        // Collapse everything, then re-open the root so the map never disappears.
        instance.expandNodeAll(instance.findEle(instance.nodeData.id), false);
        instance.expandNode(instance.findEle(instance.nodeData.id), true);
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
