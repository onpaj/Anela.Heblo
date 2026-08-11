import {
  addChildNode,
  addSiblingNode,
  childrenOf,
  deleteNode,
  indentNode,
  MindMapDocument,
  moveNode,
  outdentNode,
  parseDocument,
  renameNode,
  setAllCollapsed,
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

test("addChildNode expands a collapsed parent so the new child is visible", () => {
  const collapsed = toggleCollapsed(doc(), "a");
  const { doc: updated } = addChildNode(collapsed, "a", "Nové dítě");
  expect(updated.nodes.find((n) => n.id === "a")!.collapsed).toBe(false);
});

// --- sibling order and structure editing ---

const siblingsDoc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "A", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "b", parentId: "root", title: "B", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "c", parentId: "root", title: "C", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
  ],
  suppressedNodes: [],
});

const siblingIds = (d: MindMapDocument, parentId: string) => childrenOf(d, parentId).map((n) => n.id);

test("addSiblingNode inserts directly after the reference node, not at the end", () => {
  const { doc: updated, newNodeId } = addSiblingNode(siblingsDoc(), "a", "Nový");
  expect(siblingIds(updated, "root")).toEqual(["a", newNodeId, "b", "c"]);
});

test("addSiblingNode on the root falls back to adding a child", () => {
  const { doc: updated, newNodeId } = addSiblingNode(siblingsDoc(), "root", "Nový");
  expect(updated.nodes.find((n) => n.id === newNodeId)!.parentId).toBe("root");
});

test("moveNode reorders a node among its siblings", () => {
  expect(siblingIds(moveNode(siblingsDoc(), "c", -1), "root")).toEqual(["a", "c", "b"]);
  expect(siblingIds(moveNode(siblingsDoc(), "a", 1), "root")).toEqual(["b", "a", "c"]);
});

test("moveNode returns the document unchanged at the ends of the sibling list", () => {
  const original = siblingsDoc();
  expect(moveNode(original, "a", -1)).toBe(original);
  expect(moveNode(original, "c", 1)).toBe(original);
});

test("moveNode leaves other branches' array positions untouched", () => {
  const withChild = addChildNode(siblingsDoc(), "b", "dítě").doc;
  const moved = moveNode(withChild, "c", -1);
  expect(siblingIds(moved, "b")).toEqual(siblingIds(withChild, "b"));
});

test("indentNode makes a node the last child of its previous sibling", () => {
  const withChild = addChildNode(siblingsDoc(), "a", "existující").doc;
  const indented = indentNode(withChild, "b");
  expect(indented.nodes.find((n) => n.id === "b")!.parentId).toBe("a");
  expect(siblingIds(indented, "a").at(-1)).toBe("b");
  expect(siblingIds(indented, "root")).toEqual(["a", "c"]);
});

test("indentNode expands the new parent so the demoted node stays visible", () => {
  const collapsedParent = toggleCollapsed(siblingsDoc(), "a");
  expect(indentNode(collapsedParent, "b").nodes.find((n) => n.id === "a")!.collapsed).toBe(false);
});

test("indentNode does nothing for the first sibling or the root", () => {
  const original = siblingsDoc();
  expect(indentNode(original, "a")).toBe(original);
  expect(indentNode(original, "root")).toBe(original);
});

test("outdentNode promotes a node to sit right after its former parent", () => {
  const nested = indentNode(siblingsDoc(), "b"); // b becomes a's child
  const promoted = outdentNode(nested, "b");
  expect(promoted.nodes.find((n) => n.id === "b")!.parentId).toBe("root");
  expect(siblingIds(promoted, "root")).toEqual(["a", "b", "c"]);
});

test("outdentNode refuses to promote a top-level branch — that would be a second root", () => {
  const original = siblingsDoc();
  expect(outdentNode(original, "a")).toBe(original);
  expect(outdentNode(original, "root")).toBe(original);
});

test("setAllCollapsed collapses every parent but always leaves the root open", () => {
  const collapsed = setAllCollapsed(doc(), true);
  expect(collapsed.nodes.find((n) => n.id === "root")!.collapsed).toBe(false);
  expect(collapsed.nodes.find((n) => n.id === "a")!.collapsed).toBe(true);
  expect(collapsed.nodes.find((n) => n.id === "b")!.collapsed).toBe(false); // leaf, nothing to collapse
});

test("setAllCollapsed(false) expands everything", () => {
  const expanded = setAllCollapsed(setAllCollapsed(doc(), true), false);
  expect(expanded.nodes.every((n) => !n.collapsed)).toBe(true);
});

test("visibleNodeIds hides descendants of collapsed nodes", () => {
  const collapsed = toggleCollapsed(doc(), "a");
  expect(visibleNodeIds(collapsed)).toEqual(new Set(["root", "a"]));
});

test("toggleCollapsed flips collapsed on and back off", () => {
  const collapsed = toggleCollapsed(doc(), "a");
  expect(collapsed.nodes.find((n) => n.id === "a")!.collapsed).toBe(true);
  const expanded = toggleCollapsed(collapsed, "a");
  expect(expanded.nodes.find((n) => n.id === "a")!.collapsed).toBe(false);
});

test("toggleCollapsed returns the document unchanged for an unknown node id", () => {
  const original = doc();
  const result = toggleCollapsed(original, "does-not-exist");
  expect(result).toBe(original);
});

// Two nodes that are each other's parent. The document validator rejects this
// server-side, but client code must still degrade gracefully instead of
// hanging the browser tab if it ever sees one (e.g. a bug elsewhere in the app).
const cyclicDoc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Root", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "x", parentId: "y", title: "X", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "y", parentId: "x", title: "Y", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
  ],
  suppressedNodes: [],
});

test(
  "visibleNodeIds terminates on a document with a parent cycle",
  () => {
    const result = visibleNodeIds(cyclicDoc());
    expect(result).toBeInstanceOf(Set);
  },
  2000,
);

test(
  "deleteNode terminates on a document with a parent cycle",
  () => {
    const result = deleteNode(cyclicDoc(), "x");
    expect(Array.isArray(result.nodes)).toBe(true);
  },
  2000,
);
