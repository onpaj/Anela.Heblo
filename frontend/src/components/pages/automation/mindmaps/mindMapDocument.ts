// TS mirror of the backend MindMapDocument JSON contract (camelCase).
// All helpers are pure: they return a NEW document and never mutate inputs.

export type MindMapNodeStatus = "active" | "done" | "blocked" | "idea";

export interface MindMapNodePosition {
  x: number;
  y: number;
}

export interface MindMapNode {
  id: string;
  parentId: string | null;
  title: string;
  notes: string | null;
  status: MindMapNodeStatus;
  owner: string | null;
  lockedBy: string | null;
  sourceMeetingIds: string[];
  position: MindMapNodePosition | null;
  collapsed: boolean;
}

export interface SuppressedNode {
  title: string;
  deletedBy: string | null;
}

export interface MindMapDocument {
  schemaVersion: number;
  rootNodeId: string;
  nodes: MindMapNode[];
  suppressedNodes: SuppressedNode[];
}

export function parseDocument(json: string): MindMapDocument {
  const parsed = JSON.parse(json) as MindMapDocument;
  if (!parsed || !Array.isArray(parsed.nodes) || !parsed.rootNodeId) {
    throw new Error("Invalid mind map document");
  }
  return parsed;
}

function withNodes(doc: MindMapDocument, nodes: MindMapNode[]): MindMapDocument {
  return { ...doc, nodes };
}

function patchNode(
  doc: MindMapDocument,
  nodeId: string,
  patch: Partial<MindMapNode>,
): MindMapDocument {
  return withNodes(
    doc,
    doc.nodes.map((n) => (n.id === nodeId ? { ...n, ...patch } : n)),
  );
}

export function renameNode(doc: MindMapDocument, nodeId: string, title: string): MindMapDocument {
  return patchNode(doc, nodeId, { title });
}

export function updateNodeFields(
  doc: MindMapDocument,
  nodeId: string,
  patch: Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>,
): MindMapDocument {
  return patchNode(doc, nodeId, patch);
}

export function setNodePosition(
  doc: MindMapDocument,
  nodeId: string,
  position: MindMapNodePosition,
): MindMapDocument {
  return patchNode(doc, nodeId, { position });
}

export function toggleCollapsed(doc: MindMapDocument, nodeId: string): MindMapDocument {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node) return doc;
  return patchNode(doc, nodeId, { collapsed: !node.collapsed });
}

export function addChildNode(
  doc: MindMapDocument,
  parentId: string,
  title: string,
): { doc: MindMapDocument; newNodeId: string } {
  const newNodeId = `tmp-${Math.random().toString(36).slice(2)}${Math.random().toString(36).slice(2)}`;
  const node: MindMapNode = {
    id: newNodeId,
    parentId,
    title,
    notes: null,
    status: "active",
    owner: null,
    lockedBy: null,
    sourceMeetingIds: [],
    position: null,
    collapsed: false,
  };
  return { doc: withNodes(doc, [...doc.nodes, node]), newNodeId };
}

function descendantIds(doc: MindMapDocument, nodeId: string): Set<string> {
  const childrenByParent = new Map<string, string[]>();
  for (const node of doc.nodes) {
    if (node.parentId) {
      const siblings = childrenByParent.get(node.parentId) ?? [];
      childrenByParent.set(node.parentId, [...siblings, node.id]);
    }
  }
  const result = new Set<string>();
  const queue = [nodeId];
  while (queue.length > 0) {
    const current = queue.shift()!;
    result.add(current);
    for (const child of childrenByParent.get(current) ?? []) queue.push(child);
  }
  return result;
}

export function deleteNode(doc: MindMapDocument, nodeId: string): MindMapDocument {
  if (nodeId === doc.rootNodeId) return doc;
  const toRemove = descendantIds(doc, nodeId);
  return withNodes(doc, doc.nodes.filter((n) => !toRemove.has(n.id)));
}

/** Ids of nodes whose ancestors are all expanded (collapsed nodes stay visible, their subtrees hide). */
export function visibleNodeIds(doc: MindMapDocument): Set<string> {
  const byId = new Map(doc.nodes.map((n) => [n.id, n]));
  const visible = new Set<string>();
  for (const node of doc.nodes) {
    let ancestor = node.parentId ? byId.get(node.parentId) : null;
    let hidden = false;
    while (ancestor) {
      if (ancestor.collapsed) {
        hidden = true;
        break;
      }
      ancestor = ancestor.parentId ? byId.get(ancestor.parentId) : null;
    }
    if (!hidden) visible.add(node.id);
  }
  return visible;
}
