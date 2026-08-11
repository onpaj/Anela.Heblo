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

/** The subset of a node the user can edit by hand. */
export type MindMapNodePatch = Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>;

export function parseDocument(json: string): MindMapDocument {
  const parsed = JSON.parse(json) as MindMapDocument;
  if (!parsed || !Array.isArray(parsed.nodes) || !parsed.rootNodeId) {
    throw new Error("Invalid mind map document");
  }
  // toMindElixir throws if rootNodeId names no node, and it is called unguarded
  // from MindMapCanvas's mount and revision effects. Catching the same defect here
  // — where every caller already has a try/catch and an error state to fall back
  // on — keeps a malformed document from escaping as an uncaught exception later.
  if (!parsed.nodes.some((n) => n.id === parsed.rootNodeId)) {
    throw new Error("Invalid mind map document: no node matches rootNodeId");
  }
  return parsed;
}
