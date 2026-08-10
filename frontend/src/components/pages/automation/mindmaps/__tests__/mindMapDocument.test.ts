import {
  addChildNode,
  deleteNode,
  MindMapDocument,
  parseDocument,
  renameNode,
  setNodePosition,
  toggleCollapsed,
  updateNodeFields,
  visibleNodeIds,
} from "../mindMapDocument";

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "Větev A", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "b", parentId: "a", title: "List B", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
  ],
  suppressedNodes: [],
});

test("parseDocument throws on malformed json", () => {
  expect(() => parseDocument("not json")).toThrow();
});

test("renameNode returns new doc without mutating the original", () => {
  const original = doc();
  const renamed = renameNode(original, "a", "Nový název");
  expect(renamed.nodes.find((n) => n.id === "a")!.title).toBe("Nový název");
  expect(original.nodes.find((n) => n.id === "a")!.title).toBe("Větev A");
});

test("updateNodeFields patches only given fields", () => {
  const updated = updateNodeFields(doc(), "a", { status: "blocked", owner: "Ondra" });
  const a = updated.nodes.find((n) => n.id === "a")!;
  expect(a.status).toBe("blocked");
  expect(a.owner).toBe("Ondra");
  expect(a.title).toBe("Větev A");
});

test("addChildNode appends a node with a tmp- id under the parent", () => {
  const { doc: updated, newNodeId } = addChildNode(doc(), "a", "Nové dítě");
  const added = updated.nodes.find((n) => n.id === newNodeId)!;
  expect(newNodeId.startsWith("tmp-")).toBe(true);
  expect(added.parentId).toBe("a");
  expect(added.title).toBe("Nové dítě");
});

test("deleteNode removes the node and its descendants, never the root", () => {
  const updated = deleteNode(doc(), "a");
  expect(updated.nodes.map((n) => n.id)).toEqual(["root"]);
  expect(deleteNode(doc(), "root").nodes).toHaveLength(3);
});

test("setNodePosition stores the dragged position", () => {
  const updated = setNodePosition(doc(), "b", { x: 10, y: 20 });
  expect(updated.nodes.find((n) => n.id === "b")!.position).toEqual({ x: 10, y: 20 });
});

test("visibleNodeIds hides descendants of collapsed nodes", () => {
  const collapsed = toggleCollapsed(doc(), "a");
  expect(visibleNodeIds(collapsed)).toEqual(new Set(["root", "a"]));
});
