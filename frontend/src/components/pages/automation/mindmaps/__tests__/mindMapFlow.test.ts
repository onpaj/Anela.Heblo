import { MindMapDocument } from "../mindMapDocument";
import { toFlowGraph } from "../mindMapFlow";

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "Větev", notes: null, status: "done", owner: null, lockedBy: "ondra@anela.cz", sourceMeetingIds: [], position: { x: 300, y: 40 }, collapsed: false },
  ],
  suppressedNodes: [],
});

test("toFlowGraph produces one flow node per visible doc node and edges to parents", () => {
  const { nodes, edges } = toFlowGraph(doc());
  expect(nodes).toHaveLength(2);
  expect(edges).toEqual([
    expect.objectContaining({ source: "root", target: "a" }),
  ]);
});

test("toFlowGraph keeps saved positions and lays out unsaved ones", () => {
  const { nodes } = toFlowGraph(doc());
  const a = nodes.find((n) => n.id === "a")!;
  const root = nodes.find((n) => n.id === "root")!;
  expect(a.position).toEqual({ x: 300, y: 40 });
  expect(Number.isFinite(root.position.x)).toBe(true);
});

test("toFlowGraph passes lock and status into node data", () => {
  const { nodes } = toFlowGraph(doc());
  const a = nodes.find((n) => n.id === "a")!;
  expect(a.data).toEqual(
    expect.objectContaining({ title: "Větev", status: "done", isLocked: true }),
  );
});

test("toFlowGraph flags isRoot and childCount for the root and its child", () => {
  const { nodes } = toFlowGraph(doc());
  const root = nodes.find((n) => n.id === "root")!;
  const a = nodes.find((n) => n.id === "a")!;
  expect(root.data).toEqual(
    expect.objectContaining({ isRoot: true, collapsed: false, childCount: 1 }),
  );
  expect(a.data).toEqual(
    expect.objectContaining({ isRoot: false, collapsed: false, childCount: 0 }),
  );
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
  const { nodes, edges } = toFlowGraph(collapsedParentDoc());
  expect(nodes.map((n) => n.id).sort()).toEqual(["a", "root"]);
  expect(edges).toEqual([expect.objectContaining({ source: "root", target: "a" })]);
});

test("toFlowGraph counts children from all nodes, not just visible ones", () => {
  const { nodes } = toFlowGraph(collapsedParentDoc());
  const a = nodes.find((n) => n.id === "a")!;
  // "b" is hidden because "a" is collapsed, but childCount must still be 1
  // so the collapsed node keeps showing its expand affordance.
  expect(a.data).toEqual(expect.objectContaining({ collapsed: true, childCount: 1 }));
});
