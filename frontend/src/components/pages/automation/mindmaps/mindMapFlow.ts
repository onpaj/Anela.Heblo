// Adapter from the pure two-sided layout (mindMapLayout.ts) to React Flow's
// node/edge shapes. React Flow contributes the viewport — pan, zoom, fitView —
// and nothing else: every position here comes from our own layout.

import type { Edge } from "@xyflow/react";
import type { Node } from "@xyflow/react";
import { MindMapDocument, MindMapNodeStatus } from "./mindMapDocument";
import { layoutMindMap, MindMapSide } from "./mindMapLayout";
import type { MeasureText } from "./mindMapTextMeasure";
import type { NodeTier } from "./mindMapTheme";

export interface MindMapFlowData extends Record<string, unknown> {
  lines: string[];
  tier: NodeTier;
  side: MindMapSide;
  color: string;
  width: number;
  height: number;
  ownerBadge: string | null;
  hasNotes: boolean;
  isLocked: boolean;
  isRoot: boolean;
  collapsed: boolean;
  childCount: number;
  status: MindMapNodeStatus;
  /** Plain title (unwrapped) — used by the inline editor and for a11y labels. */
  title: string;
}

export interface MindMapFlowEdgeData extends Record<string, unknown> {
  color: string;
  isRootEdge: boolean;
}

export type MindMapFlowNode = Node<MindMapFlowData>;
export type MindMapFlowEdge = Edge<MindMapFlowEdgeData>;

export const MIND_MAP_EDGE_TYPE = "mindMapEdge";
export const MIND_MAP_NODE_TYPE = "mindMapNode";

// Every card exposes handles on both sides; the edge picks the pair that matches the
// side its target sits on, so the curve always leaves the parent's outward edge and
// enters the child's inward edge.
export const HANDLE_IDS = {
  sourceLeft: "source-left",
  sourceRight: "source-right",
  targetLeft: "target-left",
  targetRight: "target-right",
} as const;

export function toFlowGraph(
  doc: MindMapDocument,
  measure?: MeasureText,
): { nodes: MindMapFlowNode[]; edges: MindMapFlowEdge[] } {
  const { cards, connectors } = layoutMindMap(doc, measure);
  const titleById = new Map(doc.nodes.map((n) => [n.id, n.title]));

  const nodes: MindMapFlowNode[] = cards.map((card) => ({
    id: card.id,
    type: MIND_MAP_NODE_TYPE,
    position: { x: card.x, y: card.y },
    // React Flow reads these for edge anchoring before the DOM is measured.
    width: card.width,
    height: card.height,
    data: {
      lines: card.lines,
      tier: card.tier,
      side: card.side,
      color: card.color,
      width: card.width,
      height: card.height,
      ownerBadge: card.ownerBadge,
      hasNotes: card.hasNotes,
      isLocked: card.isLocked,
      isRoot: card.depth === 0,
      collapsed: card.collapsed,
      childCount: card.childCount,
      status: card.status,
      title: titleById.get(card.id) ?? card.lines.join("\n"),
    },
  }));

  const edges: MindMapFlowEdge[] = connectors.map((connector) => ({
    id: connector.id,
    source: connector.sourceId,
    target: connector.targetId,
    type: MIND_MAP_EDGE_TYPE,
    sourceHandle: connector.side === 1 ? HANDLE_IDS.sourceRight : HANDLE_IDS.sourceLeft,
    targetHandle: connector.side === 1 ? HANDLE_IDS.targetLeft : HANDLE_IDS.targetRight,
    data: { color: connector.color, isRootEdge: connector.isRootEdge },
  }));

  return { nodes, edges };
}
