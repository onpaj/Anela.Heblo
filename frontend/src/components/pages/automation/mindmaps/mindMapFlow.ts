import dagre from "dagre";
import type { Edge, Node } from "@xyflow/react";
import { MindMapDocument, MindMapNodeStatus, visibleNodeIds } from "./mindMapDocument";

export interface MindMapFlowData extends Record<string, unknown> {
  title: string;
  status: MindMapNodeStatus;
  owner: string | null;
  isLocked: boolean;
  isRoot: boolean;
  collapsed: boolean;
  childCount: number;
}

export type MindMapFlowNode = Node<MindMapFlowData>;

const NODE_WIDTH = 220;
const NODE_HEIGHT = 64;

export function toFlowGraph(doc: MindMapDocument): {
  nodes: MindMapFlowNode[];
  edges: Edge[];
} {
  const visible = visibleNodeIds(doc);
  const visibleNodes = doc.nodes.filter((n) => visible.has(n.id));

  // Auto-layout (left-to-right tree) for every visible node; saved positions win.
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir: "LR", nodesep: 24, ranksep: 80 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const node of visibleNodes) {
    graph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  }
  for (const node of visibleNodes) {
    if (node.parentId && visible.has(node.parentId)) {
      graph.setEdge(node.parentId, node.id);
    }
  }
  dagre.layout(graph);

  const childCount = new Map<string, number>();
  for (const node of doc.nodes) {
    if (node.parentId) {
      childCount.set(node.parentId, (childCount.get(node.parentId) ?? 0) + 1);
    }
  }

  const nodes: MindMapFlowNode[] = visibleNodes.map((node) => {
    const layouted = graph.node(node.id);
    return {
      id: node.id,
      type: "mindMapNode",
      position: node.position ?? {
        x: layouted.x - NODE_WIDTH / 2,
        y: layouted.y - NODE_HEIGHT / 2,
      },
      data: {
        title: node.title,
        status: node.status,
        owner: node.owner,
        isLocked: node.lockedBy !== null,
        isRoot: node.id === doc.rootNodeId,
        collapsed: node.collapsed,
        childCount: childCount.get(node.id) ?? 0,
      },
    };
  });

  const edges: Edge[] = visibleNodes
    .filter((n) => n.parentId && visible.has(n.parentId))
    .map((n) => ({
      id: `${n.parentId}->${n.id}`,
      source: n.parentId!,
      target: n.id,
      type: "smoothstep",
    }));

  return { nodes, edges };
}
