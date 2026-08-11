// Two-sided mind map layout, ported from the "Anela — otevřená témata" HTML template:
// the root sits in the middle, its first half of branches grows to the right and the
// rest to the left, and every subtree is centred against its own children.
//
// Pure and DOM-free: text measurement is injected, so the same code lays out in the
// browser (canvas metrics) and in tests (character estimate). The layout also owns
// line breaking — it hands each card the exact lines to paint — so the geometry it
// computes and the geometry the browser paints can never drift apart.

import { childrenOf, MindMapDocument, MindMapNode, visibleNodeIds } from "./mindMapDocument";
import { MeasureText, measureTextWidth } from "./mindMapTextMeasure";
import {
  branchColorAt,
  COUNT_BADGE_EXTRA_WIDTH,
  COUNT_BADGE_FONT,
  fontOf,
  LEVEL_GAP,
  LOCK_ICON_WIDTH,
  MAX_CONTENT_WIDTH,
  NEUTRAL_BRANCH_COLOR,
  NodeTier,
  NOTE_BADGE_EXTRA_WIDTH,
  NOTE_BADGE_FONT,
  ROW_GAP,
  TIER_METRICS,
  tierOf,
} from "./mindMapTheme";

/** -1 = left of the root, +1 = right of it. The root itself is +1 by convention. */
export type MindMapSide = -1 | 1;

export interface MindMapCard {
  id: string;
  depth: number;
  tier: NodeTier;
  side: MindMapSide;
  /** Top-left corner, root-centred coordinate space. */
  x: number;
  y: number;
  width: number;
  height: number;
  /** Pre-wrapped title lines — render with `white-space: pre`, never re-wrap. */
  lines: string[];
  /** Branch colour inherited from the top-level ancestor. */
  color: string;
  ownerBadge: string | null;
  hasNotes: boolean;
  isLocked: boolean;
  collapsed: boolean;
  childCount: number;
  status: MindMapNode["status"];
}

export interface MindMapConnector {
  id: string;
  sourceId: string;
  targetId: string;
  side: MindMapSide;
  color: string;
  /** Edges leaving the root are drawn heavier and more opaque, as in the template. */
  isRootEdge: boolean;
}

export interface MindMapLayout {
  cards: MindMapCard[];
  connectors: MindMapConnector[];
}

/** Index of the first top-level branch that grows to the LEFT of the root. */
function leftSplitIndex(branchCount: number): number {
  return Math.ceil(branchCount / 2);
}

/**
 * Which side of the root a node lives on. Arrow-key navigation needs this without
 * running a full layout: on the left half, "out" (toward the parent) is ArrowRight.
 */
export function branchSideOf(doc: MindMapDocument, nodeId: string): MindMapSide {
  const byId = new Map(doc.nodes.map((n) => [n.id, n]));
  const branches = childrenOf(doc, doc.rootNodeId);

  let current = byId.get(nodeId);
  const seen = new Set<string>();
  while (current && current.parentId && current.parentId !== doc.rootNodeId) {
    if (seen.has(current.id)) break; // cycle guard
    seen.add(current.id);
    current = byId.get(current.parentId);
  }
  if (!current) return 1;

  const index = branches.findIndex((b) => b.id === current!.id);
  if (index === -1) return 1;
  return index < leftSplitIndex(branches.length) ? 1 : -1;
}

function groupChildrenByParent(nodes: MindMapNode[]): Map<string, MindMapNode[]> {
  const byParent = new Map<string, MindMapNode[]>();
  for (const node of nodes) {
    if (!node.parentId) continue;
    const siblings = byParent.get(node.parentId);
    // Sibling order is the document's array order — the same ordering the
    // ⌘↑/⌘↓ reorder commands rewrite, and the one the backend round-trips.
    byParent.set(node.parentId, siblings ? [...siblings, node] : [node]);
  }
  return byParent;
}

function wrapLine(text: string, font: string, measure: MeasureText): string[] {
  if (measure(text, font) <= MAX_CONTENT_WIDTH) return [text];

  const words = text.split(" ");
  const lines: string[] = [];
  let current = "";
  for (const word of words) {
    const candidate = current ? `${current} ${word}` : word;
    if (current && measure(candidate, font) > MAX_CONTENT_WIDTH) {
      lines.push(current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current) lines.push(current);
  return lines.length > 0 ? lines : [text];
}

function wrapTitle(title: string, font: string, measure: MeasureText): string[] {
  return title.split("\n").flatMap((line) => wrapLine(line, font, measure));
}

interface CardMetrics {
  width: number;
  height: number;
  lines: string[];
}

function measureCard(
  node: MindMapNode,
  tier: NodeTier,
  childCount: number,
  measure: MeasureText,
): CardMetrics {
  const metrics = TIER_METRICS[tier];
  const font = fontOf(tier);
  const lines = wrapTitle(node.title, font, measure);

  // Badges and glyphs sit inline after the last line, exactly as they render.
  let trailingWidth = 0;
  if (node.owner) trailingWidth += measure(node.owner, NOTE_BADGE_FONT) + NOTE_BADGE_EXTRA_WIDTH;
  if (node.collapsed && childCount > 0) {
    trailingWidth += measure(String(childCount), COUNT_BADGE_FONT) + COUNT_BADGE_EXTRA_WIDTH;
  }
  if (node.lockedBy) trailingWidth += LOCK_ICON_WIDTH;
  if (node.notes) trailingWidth += LOCK_ICON_WIDTH;

  const lineWidths = lines.map((line, i) =>
    measure(line, font) + (i === lines.length - 1 ? trailingWidth : 0),
  );
  const contentWidth = Math.min(Math.max(...lineWidths, 0), MAX_CONTENT_WIDTH + trailingWidth);

  const chrome = 2 * metrics.paddingX + 2 * metrics.borderWidth;
  return {
    width: Math.ceil(contentWidth + chrome),
    height: Math.ceil(lines.length * metrics.lineHeight + 2 * metrics.paddingY + 2 * metrics.borderWidth),
    lines,
  };
}

interface Placed extends CardMetrics {
  node: MindMapNode;
  depth: number;
  children: Placed[];
  /** Height of this node's whole subtree. */
  subtreeHeight: number;
  centerX: number;
  centerY: number;
}

export function layoutMindMap(
  doc: MindMapDocument,
  measure: MeasureText = measureTextWidth,
): MindMapLayout {
  const visible = visibleNodeIds(doc);
  const visibleNodes = doc.nodes.filter((n) => visible.has(n.id));
  const root = visibleNodes.find((n) => n.id === doc.rootNodeId);
  if (!root) return { cards: [], connectors: [] };

  const childrenByParent = groupChildrenByParent(visibleNodes);

  const totalChildCount = new Map<string, number>();
  for (const node of doc.nodes) {
    if (node.parentId) {
      totalChildCount.set(node.parentId, (totalChildCount.get(node.parentId) ?? 0) + 1);
    }
  }

  // A collapsed node keeps its card but drops its subtree from the layout.
  const expandedChildren = (node: MindMapNode): MindMapNode[] =>
    node.collapsed ? [] : childrenByParent.get(node.id) ?? [];

  const seen = new Set<string>(); // cycle guard: a malformed parentId chain must not hang the UI
  const buildTree = (node: MindMapNode, depth: number): Placed => {
    seen.add(node.id);
    const tier = tierOf(depth);
    const metrics = measureCard(node, tier, totalChildCount.get(node.id) ?? 0, measure);
    const children = expandedChildren(node)
      .filter((child) => !seen.has(child.id))
      .map((child) => buildTree(child, depth + 1));

    const stackedHeight = children.reduce((sum, c) => sum + c.subtreeHeight + ROW_GAP, 0) - ROW_GAP;
    return {
      ...metrics,
      node,
      depth,
      children,
      subtreeHeight: children.length > 0 ? Math.max(metrics.height, stackedHeight) : metrics.height,
      centerX: 0,
      centerY: 0,
    };
  };

  const tree = buildTree(root, 0);

  // Split the root's branches: the first half grows right, the remainder left.
  const branches = tree.children;
  const splitAt = leftSplitIndex(branches.length);
  const rightBranches = branches.slice(0, splitAt);
  const leftBranches = branches.slice(splitAt);

  const place = (placed: Placed, edgeX: number, top: number, side: MindMapSide): void => {
    placed.centerY = top + placed.subtreeHeight / 2;
    placed.centerX = edgeX + (side * placed.width) / 2;

    const childrenHeight =
      placed.children.reduce((sum, c) => sum + c.subtreeHeight + ROW_GAP, 0) - ROW_GAP;
    let y = top + (placed.subtreeHeight - childrenHeight) / 2;
    for (const child of placed.children) {
      place(child, edgeX + side * (placed.width + LEVEL_GAP), y, side);
      y += child.subtreeHeight + ROW_GAP;
    }
  };

  const stackHeight = (list: Placed[]): number =>
    list.length > 0 ? list.reduce((sum, c) => sum + c.subtreeHeight + ROW_GAP, 0) - ROW_GAP : 0;

  tree.centerX = 0;
  tree.centerY = 0;

  let y = -stackHeight(rightBranches) / 2;
  for (const branch of rightBranches) {
    place(branch, tree.width / 2 + LEVEL_GAP, y, 1);
    y += branch.subtreeHeight + ROW_GAP;
  }
  y = -stackHeight(leftBranches) / 2;
  for (const branch of leftBranches) {
    place(branch, -tree.width / 2 - LEVEL_GAP, y, -1);
    y += branch.subtreeHeight + ROW_GAP;
  }

  const branchColorById = new Map<string, string>();
  branches.forEach((branch, index) => {
    const color = branchColorAt(index);
    const paint = (p: Placed): void => {
      branchColorById.set(p.node.id, color);
      p.children.forEach(paint);
    };
    paint(branch);
  });
  const colorOf = (nodeId: string): string => branchColorById.get(nodeId) ?? NEUTRAL_BRANCH_COLOR;

  const cards: MindMapCard[] = [];
  const connectors: MindMapConnector[] = [];

  const collect = (placed: Placed, side: MindMapSide): void => {
    cards.push({
      id: placed.node.id,
      depth: placed.depth,
      tier: tierOf(placed.depth),
      side,
      x: placed.centerX - placed.width / 2,
      y: placed.centerY - placed.height / 2,
      width: placed.width,
      height: placed.height,
      lines: placed.lines,
      color: colorOf(placed.node.id),
      ownerBadge: placed.node.owner,
      hasNotes: Boolean(placed.node.notes),
      isLocked: placed.node.lockedBy !== null,
      collapsed: placed.node.collapsed,
      childCount: totalChildCount.get(placed.node.id) ?? 0,
      status: placed.node.status,
    });

    for (const child of placed.children) {
      const childSide = placed.depth === 0 ? (child.centerX > 0 ? 1 : -1) : side;
      connectors.push({
        id: `${placed.node.id}->${child.node.id}`,
        sourceId: placed.node.id,
        targetId: child.node.id,
        side: childSide,
        color: colorOf(child.node.id),
        isRootEdge: placed.depth === 0,
      });
      collect(child, childSide);
    }
  };
  collect(tree, 1);

  return { cards, connectors };
}
