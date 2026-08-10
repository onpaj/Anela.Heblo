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
