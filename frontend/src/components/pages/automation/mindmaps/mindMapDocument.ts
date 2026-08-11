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

export function toggleCollapsed(doc: MindMapDocument, nodeId: string): MindMapDocument {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node) return doc;
  return patchNode(doc, nodeId, { collapsed: !node.collapsed });
}

/** Collapses or expands every node that has children. The root always stays expanded. */
export function setAllCollapsed(doc: MindMapDocument, collapsed: boolean): MindMapDocument {
  const parentIds = new Set(doc.nodes.map((n) => n.parentId).filter((id): id is string => id !== null));
  return withNodes(
    doc,
    doc.nodes.map((n) =>
      parentIds.has(n.id) && n.id !== doc.rootNodeId ? { ...n, collapsed } : { ...n, collapsed: false },
    ),
  );
}

/** Children of a node, in document array order — that order IS the sibling order. */
export function childrenOf(doc: MindMapDocument, parentId: string): MindMapNode[] {
  return doc.nodes.filter((n) => n.parentId === parentId);
}

function newNode(parentId: string, title: string): MindMapNode {
  return {
    id: `tmp-${Math.random().toString(36).slice(2)}${Math.random().toString(36).slice(2)}`,
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
}

/** Moves `nodeId` to sit immediately after `anchorId` in the array, preserving all else. */
function reinsertAfter(nodes: MindMapNode[], nodeId: string, anchorId: string): MindMapNode[] {
  const node = nodes.find((n) => n.id === nodeId);
  if (!node) return nodes;
  const without = nodes.filter((n) => n.id !== nodeId);
  const anchorIndex = without.findIndex((n) => n.id === anchorId);
  if (anchorIndex === -1) return [...without, node];
  return [...without.slice(0, anchorIndex + 1), node, ...without.slice(anchorIndex + 1)];
}

export function addChildNode(
  doc: MindMapDocument,
  parentId: string,
  title: string,
): { doc: MindMapDocument; newNodeId: string } {
  const node = newNode(parentId, title);
  // A new child of a collapsed parent would be invisible the moment it is created.
  const expanded = doc.nodes.map((n) => (n.id === parentId ? { ...n, collapsed: false } : n));
  return { doc: withNodes(doc, [...expanded, node]), newNodeId: node.id };
}

/** Inserts a sibling directly after `siblingId`. The root has no siblings — it gets a child. */
export function addSiblingNode(
  doc: MindMapDocument,
  siblingId: string,
  title: string,
): { doc: MindMapDocument; newNodeId: string } {
  const sibling = doc.nodes.find((n) => n.id === siblingId);
  if (!sibling || sibling.parentId === null) return addChildNode(doc, doc.rootNodeId, title);

  const node = newNode(sibling.parentId, title);
  const index = doc.nodes.findIndex((n) => n.id === siblingId);
  const nodes = [...doc.nodes.slice(0, index + 1), node, ...doc.nodes.slice(index + 1)];
  return { doc: withNodes(doc, nodes), newNodeId: node.id };
}

/** Demotes a node to be the last child of its preceding sibling (⌘→). */
export function indentNode(doc: MindMapDocument, nodeId: string): MindMapDocument {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node || node.parentId === null) return doc;

  const siblings = childrenOf(doc, node.parentId);
  const index = siblings.findIndex((n) => n.id === nodeId);
  if (index <= 0) return doc; // nothing to become the new parent
  const newParent = siblings[index - 1];

  const reparented = doc.nodes.map((n) => {
    if (n.id === nodeId) return { ...n, parentId: newParent.id };
    if (n.id === newParent.id) return { ...n, collapsed: false };
    return n;
  });

  // Become the LAST child: anchor on the new parent's current last child, or on the
  // parent itself when it has none.
  const existingChildren = reparented.filter((n) => n.parentId === newParent.id && n.id !== nodeId);
  const anchorId = existingChildren.length > 0 ? existingChildren[existingChildren.length - 1].id : newParent.id;
  return withNodes(doc, reinsertAfter(reparented, nodeId, anchorId));
}

/** Promotes a node to sit next to its parent, one level up (⌘←). */
export function outdentNode(doc: MindMapDocument, nodeId: string): MindMapDocument {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node || node.parentId === null) return doc;

  const parent = doc.nodes.find((n) => n.id === node.parentId);
  // Outdenting a top-level branch would give the document a second root.
  if (!parent || parent.parentId === null) return doc;

  const reparented = doc.nodes.map((n) => (n.id === nodeId ? { ...n, parentId: parent.parentId } : n));
  return withNodes(doc, reinsertAfter(reparented, nodeId, parent.id));
}

/** Reorders a node among its siblings (⌘↑ / ⌘↓). `delta` is -1 or +1. */
export function moveNode(doc: MindMapDocument, nodeId: string, delta: number): MindMapDocument {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node || node.parentId === null) return doc;

  const siblings = childrenOf(doc, node.parentId);
  const from = siblings.findIndex((n) => n.id === nodeId);
  const to = from + delta;
  if (from === -1 || to < 0 || to >= siblings.length) return doc;

  const reordered = [...siblings];
  reordered.splice(to, 0, ...reordered.splice(from, 1));

  // Write the new sibling order back into the slots the siblings already occupy,
  // leaving every other node's array position untouched.
  const slots = doc.nodes.reduce<number[]>((acc, n, i) => (n.parentId === node.parentId ? [...acc, i] : acc), []);
  const nodes = [...doc.nodes];
  slots.forEach((slot, i) => {
    nodes[slot] = reordered[i];
  });
  return withNodes(doc, nodes);
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
    if (result.has(current)) continue; // cycle guard: never re-expand a visited node
    result.add(current);
    for (const child of childrenByParent.get(current) ?? []) {
      if (!result.has(child)) queue.push(child);
    }
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
    const seen = new Set<string>(); // cycle guard: stop walking once an ancestor repeats
    while (ancestor) {
      if (seen.has(ancestor.id)) break;
      seen.add(ancestor.id);
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
