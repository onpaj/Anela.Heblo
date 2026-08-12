// Conversion between our persisted MindMapDocument (flat nodes[] with parentId —
// the shape the backend validates, guards and diffs by id) and mind-elixir's
// nested NodeObj tree. Pure: no DOM, no library instance.
//
// Our extra fields ride in mind-elixir's generic `metadata` slot. `tags`, `icons`
// and `style` are DERIVED display fields — never a source of truth. They are
// recomputed by displayFieldsFor() here and again on every reshapeNode(). That
// second call is not a plain overwrite, though: mind-elixir's reshapeNode merges
// the OLD style object into the new one (`o.style && t.style && (t.style =
// Object.assign(o.style, t.style))` in its own source), so a key this function
// omits can survive from a *previous* status. displayFieldsFor defeats that by
// always emitting all three style keys — `undefined` for the ones the current
// status does not need — so the merge has nothing stale left to carry over, and
// the two truly can never disagree.

import type { MindElixirData, NodeObj } from "mind-elixir";
import { MindMapDocument, MindMapNode, MindMapNodeStatus } from "./mindMapDocument";

export interface MindMapNodeMetadata {
  status: MindMapNodeStatus;
  owner: string | null;
  lockedBy: string | null;
  sourceMeetingIds: string[];
}

export type MindMapNodeObj = NodeObj<MindMapNodeMetadata>;

const LOCK_ICON = "🔒";
const NOTE_ICON = "📝";
const IDEA_BORDER = "1px dashed #8A827B";
const BLOCKED_BORDER = "1px solid #EF4444";

export const DEFAULT_METADATA: MindMapNodeMetadata = {
  status: "active",
  owner: null,
  lockedBy: null,
  sourceMeetingIds: [],
};

export function displayFieldsFor(
  metadata: MindMapNodeMetadata,
  notes: string | null,
): Pick<MindMapNodeObj, "tags" | "icons" | "style"> {
  const icons = [
    ...(metadata.lockedBy ? [LOCK_ICON] : []),
    ...(notes ? [NOTE_ICON] : []),
  ];

  // Every key below is set on every call — even to `undefined` — for the reason
  // explained in the file header: reshapeNode's merge only clears a key if the new
  // style object actually has that key, so leaving one out lets the old value leak
  // through a status change (e.g. done -> idea keeping the strikethrough).
  const style: NonNullable<MindMapNodeObj["style"]> = {
    border:
      metadata.status === "idea" ? IDEA_BORDER : metadata.status === "blocked" ? BLOCKED_BORDER : undefined,
    color: metadata.status === "idea" ? "#8A827B" : undefined,
    textDecoration: metadata.status === "done" ? "line-through" : undefined,
  };

  return {
    tags: metadata.owner ? [metadata.owner] : undefined,
    icons: icons.length > 0 ? icons : undefined,
    style,
  };
}

export function toMindElixir(doc: MindMapDocument): MindElixirData {
  const root = doc.nodes.find((n) => n.id === doc.rootNodeId);
  if (!root) throw new Error(`Mind map document has no node for its root id '${doc.rootNodeId}'.`);

  const childrenByParent = new Map<string, MindMapNode[]>();
  for (const node of doc.nodes) {
    if (!node.parentId) continue;
    const siblings = childrenByParent.get(node.parentId);
    // Document array order IS sibling order — preserve it.
    childrenByParent.set(node.parentId, siblings ? [...siblings, node] : [node]);
  }

  const seen = new Set<string>(); // cycle guard: a malformed parentId chain must not hang the tab
  const build = (node: MindMapNode): MindMapNodeObj => {
    seen.add(node.id);
    const metadata: MindMapNodeMetadata = {
      status: node.status,
      owner: node.owner,
      lockedBy: node.lockedBy,
      sourceMeetingIds: [...node.sourceMeetingIds],
    };
    const children = (childrenByParent.get(node.id) ?? [])
      .filter((child) => !seen.has(child.id))
      .map(build);

    return {
      id: node.id,
      topic: node.title,
      note: node.notes ?? undefined,
      expanded: !node.collapsed,
      children: children.length > 0 ? children : undefined,
      metadata,
      ...displayFieldsFor(metadata, node.notes),
    };
  };

  return { nodeData: build(root) };
}

export function fromMindElixir(data: MindElixirData, previous: MindMapDocument): MindMapDocument {
  const nodes: MindMapNode[] = [];

  const walk = (obj: MindMapNodeObj, parentId: string | null): void => {
    const metadata = obj.metadata ?? DEFAULT_METADATA;
    nodes.push({
      id: obj.id,
      parentId,
      title: obj.topic,
      notes: obj.note ?? null,
      status: metadata.status ?? "active",
      owner: metadata.owner ?? null,
      lockedBy: metadata.lockedBy ?? null,
      sourceMeetingIds: metadata.sourceMeetingIds ? [...metadata.sourceMeetingIds] : [],
      // The redesign dropped manual positioning; the layout is always computed.
      position: null,
      collapsed: obj.expanded === false,
    });
    for (const child of obj.children ?? []) {
      walk(child as MindMapNodeObj, obj.id);
    }
  };
  walk(data.nodeData as MindMapNodeObj, null);

  return {
    // The library has no concept of tombstones or our schema version; both are
    // carried forward from the document the editor was loaded with.
    schemaVersion: previous.schemaVersion,
    rootNodeId: data.nodeData.id,
    nodes,
    suppressedNodes: previous.suppressedNodes,
  };
}
