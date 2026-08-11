import { MindMapDocument } from "../mindMapDocument";
import { HANDLE_IDS, MIND_MAP_EDGE_TYPE, toFlowGraph } from "../mindMapFlow";

const measure = (text: string) => text.length * 8;

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "Větev", notes: "delší poznámka", status: "done", owner: "Bára", lockedBy: "ondra@anela.cz", sourceMeetingIds: [], position: { x: 300, y: 40 }, collapsed: false },
  ],
  suppressedNodes: [],
});

test("toFlowGraph produces one flow node per visible doc node and edges to parents", () => {
  const { nodes, edges } = toFlowGraph(doc(), measure);
  expect(nodes).toHaveLength(2);
  expect(edges).toEqual([expect.objectContaining({ source: "root", target: "a" })]);
});

test("toFlowGraph ignores stored positions — the layout is always computed", () => {
  const { nodes } = toFlowGraph(doc(), measure);
  const a = nodes.find((n) => n.id === "a")!;
  // "a" carries position {300,40} in the document; dragging was removed with the
  // redesign, so that value must no longer influence where the card lands.
  expect(a.position).not.toEqual({ x: 300, y: 40 });
  expect(Number.isFinite(a.position.x)).toBe(true);
});

test("toFlowGraph passes lock, owner badge, notes and status into node data", () => {
  const { nodes } = toFlowGraph(doc(), measure);
  const a = nodes.find((n) => n.id === "a")!;
  expect(a.data).toEqual(
    expect.objectContaining({
      title: "Větev",
      status: "done",
      isLocked: true,
      ownerBadge: "Bára",
      hasNotes: true,
      tier: "branch",
    }),
  );
});

test("toFlowGraph flags isRoot and childCount for the root and its child", () => {
  const { nodes } = toFlowGraph(doc(), measure);
  expect(nodes.find((n) => n.id === "root")!.data).toEqual(
    expect.objectContaining({ isRoot: true, tier: "root", collapsed: false, childCount: 1 }),
  );
  expect(nodes.find((n) => n.id === "a")!.data).toEqual(
    expect.objectContaining({ isRoot: false, collapsed: false, childCount: 0 }),
  );
});

test("toFlowGraph uses the curved edge type and anchors handles to the branch side", () => {
  const { edges } = toFlowGraph(doc(), measure);
  expect(edges[0]).toEqual(
    expect.objectContaining({
      type: MIND_MAP_EDGE_TYPE,
      sourceHandle: HANDLE_IDS.sourceRight,
      targetHandle: HANDLE_IDS.targetLeft,
    }),
  );
});

test("toFlowGraph anchors a left-side branch to the mirrored handles", () => {
  const twoBranches: MindMapDocument = {
    ...doc(),
    nodes: [
      ...doc().nodes,
      { id: "b", parentId: "root", title: "Druhá", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    ],
  };
  const { edges } = toFlowGraph(twoBranches, measure);
  const left = edges.find((e) => e.target === "b")!;
  expect(left.sourceHandle).toBe(HANDLE_IDS.sourceLeft);
  expect(left.targetHandle).toBe(HANDLE_IDS.targetRight);
});

const collapsedParentDoc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "Větev", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: true },
    { id: "b", parentId: "a", title: "List", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
  ],
  suppressedNodes: [],
});

test("toFlowGraph hides a collapsed node's subtree and emits no dangling edge to it", () => {
  const { nodes, edges } = toFlowGraph(collapsedParentDoc(), measure);
  expect(nodes.map((n) => n.id).sort()).toEqual(["a", "root"]);
  expect(edges).toEqual([expect.objectContaining({ source: "root", target: "a" })]);
});

test("toFlowGraph counts children from all nodes, not just visible ones", () => {
  const { nodes } = toFlowGraph(collapsedParentDoc(), measure);
  const a = nodes.find((n) => n.id === "a")!;
  // "b" is hidden because "a" is collapsed, but childCount must still be 1
  // so the collapsed node keeps showing its expand affordance.
  expect(a.data).toEqual(expect.objectContaining({ collapsed: true, childCount: 1 }));
});
