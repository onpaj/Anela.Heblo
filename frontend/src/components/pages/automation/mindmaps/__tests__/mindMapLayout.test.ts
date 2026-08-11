import { MindMapDocument, MindMapNode } from "../mindMapDocument";
import { branchSideOf, layoutMindMap } from "../mindMapLayout";
import { MIND_MAP_PALETTE, NEUTRAL_BRANCH_COLOR } from "../mindMapTheme";

// Deterministic measurement so the assertions describe the layout, not the font stack.
const measure = (text: string) => text.length * 8;

function node(id: string, parentId: string | null, overrides: Partial<MindMapNode> = {}): MindMapNode {
  return {
    id,
    parentId,
    title: id,
    notes: null,
    status: "active",
    owner: null,
    lockedBy: null,
    sourceMeetingIds: [],
    position: null,
    collapsed: false,
    ...overrides,
  };
}

function doc(nodes: MindMapNode[]): MindMapDocument {
  return { schemaVersion: 1, rootNodeId: "root", nodes, suppressedNodes: [] };
}

const fourBranches = () =>
  doc([
    node("root", null),
    node("a", "root"),
    node("b", "root"),
    node("c", "root"),
    node("d", "root"),
  ]);

describe("layoutMindMap", () => {
  it("centres the root at the origin", () => {
    const { cards } = layoutMindMap(fourBranches(), measure);
    const root = cards.find((c) => c.id === "root")!;
    expect(root.x + root.width / 2).toBeCloseTo(0);
    expect(root.y + root.height / 2).toBeCloseTo(0);
  });

  it("splits top-level branches so the first half grows right and the rest left", () => {
    const { cards } = layoutMindMap(fourBranches(), measure);
    const sideOf = (id: string) => cards.find((c) => c.id === id)!.side;
    expect([sideOf("a"), sideOf("b")]).toEqual([1, 1]);
    expect([sideOf("c"), sideOf("d")]).toEqual([-1, -1]);
  });

  it("places left-side branches at negative x and right-side ones at positive x", () => {
    const { cards } = layoutMindMap(fourBranches(), measure);
    expect(cards.find((c) => c.id === "a")!.x).toBeGreaterThan(0);
    expect(cards.find((c) => c.id === "d")!.x).toBeLessThan(0);
  });

  it("colours each top-level branch from the palette and inherits it down the subtree", () => {
    const layout = layoutMindMap(
      doc([node("root", null), node("a", "root"), node("b", "root"), node("a1", "a")]),
      measure,
    );
    const colorOf = (id: string) => layout.cards.find((c) => c.id === id)!.color;
    expect(colorOf("a")).toBe(MIND_MAP_PALETTE[0]);
    expect(colorOf("a1")).toBe(MIND_MAP_PALETTE[0]);
    expect(colorOf("b")).toBe(MIND_MAP_PALETTE[1]);
    expect(colorOf("root")).toBe(NEUTRAL_BRANCH_COLOR);
  });

  it("marks connectors leaving the root so they can be drawn heavier", () => {
    const { connectors } = layoutMindMap(
      doc([node("root", null), node("a", "root"), node("a1", "a")]),
      measure,
    );
    expect(connectors.find((c) => c.targetId === "a")!.isRootEdge).toBe(true);
    expect(connectors.find((c) => c.targetId === "a1")!.isRootEdge).toBe(false);
  });

  it("drops a collapsed node's subtree but keeps its own card and child count", () => {
    const { cards, connectors } = layoutMindMap(
      doc([node("root", null), node("a", "root", { collapsed: true }), node("a1", "a")]),
      measure,
    );
    expect(cards.map((c) => c.id).sort()).toEqual(["a", "root"]);
    expect(cards.find((c) => c.id === "a")!.childCount).toBe(1);
    expect(connectors).toHaveLength(1);
  });

  it("wraps a long title into several lines instead of one very wide card", () => {
    const longTitle = Array.from({ length: 40 }, () => "slovo").join(" ");
    const { cards } = layoutMindMap(doc([node("root", null, { title: longTitle })]), measure);
    expect(cards[0].lines.length).toBeGreaterThan(1);
  });

  it("keeps explicit line breaks from the title", () => {
    const { cards } = layoutMindMap(doc([node("root", null, { title: "Anela\notevřená témata" })]), measure);
    expect(cards[0].lines).toEqual(["Anela", "otevřená témata"]);
  });

  it("never overlaps two siblings vertically", () => {
    const { cards } = layoutMindMap(fourBranches(), measure);
    const right = cards.filter((c) => c.id === "a" || c.id === "b").sort((x, y) => x.y - y.y);
    expect(right[0].y + right[0].height).toBeLessThanOrEqual(right[1].y);
  });

  it("returns an empty layout when the root id is missing rather than throwing", () => {
    expect(layoutMindMap(doc([node("orphan", null)]), measure)).toEqual({ cards: [], connectors: [] });
  });

  it("terminates on a parent/child cycle instead of hanging", () => {
    const cyclic = doc([node("root", null), node("a", "root"), node("b", "a"), node("a2", "b")]);
    cyclic.nodes[1] = { ...cyclic.nodes[1], parentId: "a2" }; // a -> a2 -> b -> a
    expect(() => layoutMindMap(cyclic, measure)).not.toThrow();
  });
});

describe("branchSideOf", () => {
  it("agrees with the side the layout assigns", () => {
    const document = fourBranches();
    const { cards } = layoutMindMap(document, measure);
    for (const card of cards.filter((c) => c.id !== "root")) {
      expect(branchSideOf(document, card.id)).toBe(card.side);
    }
  });

  it("reports a deep descendant on the same side as its top-level branch", () => {
    const document = doc([
      node("root", null),
      node("a", "root"),
      node("b", "root"),
      node("b1", "b"),
      node("b2", "b1"),
    ]);
    expect(branchSideOf(document, "b2")).toBe(-1);
  });
});
