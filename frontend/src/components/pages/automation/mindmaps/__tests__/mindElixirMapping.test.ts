import { MindMapDocument, MindMapNode } from "../mindMapDocument";
import { displayFieldsFor, fromMindElixir, toMindElixir } from "../mindElixirMapping";

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

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    node("root", null, { title: "Anela\notevřená témata" }),
    node("a", "root", { title: "Cílovky", owner: "Bára", status: "idea" }),
    node("b", "root", { title: "Parkoviště", collapsed: true, lockedBy: "ondra@anela.cz" }),
    node("a1", "a", { title: "35–45", notes: "delší poznámka", sourceMeetingIds: ["m1", "m2"] }),
    node("a2", "a", { title: "Precedens", status: "done" }),
  ],
  suppressedNodes: [{ title: "Smazané téma", deletedBy: "ondra@anela.cz" }],
});

test("toMindElixir nests children under their parent in document order", () => {
  const data = toMindElixir(doc());
  expect(data.nodeData.id).toBe("root");
  expect(data.nodeData.children?.map((c) => c.id)).toEqual(["a", "b"]);
  const a = data.nodeData.children![0];
  expect(a.children?.map((c) => c.id)).toEqual(["a1", "a2"]);
});

test("toMindElixir maps title, notes and collapsed onto topic, note and expanded", () => {
  const data = toMindElixir(doc());
  expect(data.nodeData.topic).toBe("Anela\notevřená témata");
  const [a, b] = data.nodeData.children!;
  expect(a.expanded).toBe(true);
  expect(b.expanded).toBe(false);
  expect(a.children![0].note).toBe("delší poznámka");
});

test("toMindElixir carries our extra fields in metadata", () => {
  const data = toMindElixir(doc());
  const a = data.nodeData.children![0];
  expect(a.metadata).toEqual({
    status: "idea",
    owner: "Bára",
    lockedBy: null,
    sourceMeetingIds: [],
  });
  expect(a.children![0].metadata?.sourceMeetingIds).toEqual(["m1", "m2"]);
});

// A round trip returns the flat array in depth-first order (a parent immediately
// followed by its subtree) rather than the order it went in. That is a reordering
// of the array, not a loss: what carries meaning is each parent's sibling order,
// and nothing — not the layout, not MindMapGuard, not MindMapLockService, all of
// which key by id — reads the absolute index. Compare accordingly.
const nodesById = (d: MindMapDocument) => Object.fromEntries(d.nodes.map((n) => [n.id, n]));
const siblingOrder = (d: MindMapDocument, parentId: string | null) =>
  d.nodes.filter((n) => n.parentId === parentId).map((n) => n.id);

test("a document round-trips through mind-elixir without losing a field", () => {
  const original = doc();
  const restored = fromMindElixir(toMindElixir(original), original);
  expect(restored.nodes).toHaveLength(original.nodes.length);
  expect(nodesById(restored)).toEqual(nodesById(original));
  expect(restored.schemaVersion).toBe(original.schemaVersion);
  expect(restored.rootNodeId).toBe(original.rootNodeId);
  expect(restored.suppressedNodes).toEqual(original.suppressedNodes);
});

test("round-trip preserves every parent's sibling order", () => {
  const original = doc();
  const restored = fromMindElixir(toMindElixir(original), original);
  expect(siblingOrder(restored, null)).toEqual(siblingOrder(original, null));
  expect(siblingOrder(restored, "root")).toEqual(siblingOrder(original, "root"));
  expect(siblingOrder(restored, "a")).toEqual(siblingOrder(original, "a"));
});

test("fromMindElixir defaults a node mind-elixir created itself", () => {
  const data = toMindElixir(doc());
  // Mimic mind-elixir's addChild: a node with an id and topic and nothing else.
  data.nodeData.children!.push({ id: "me-generated", topic: "Nový uzel" });
  const restored = fromMindElixir(data, doc());
  const added = restored.nodes.find((n) => n.id === "me-generated")!;
  expect(added).toEqual(
    expect.objectContaining({
      parentId: "root",
      title: "Nový uzel",
      status: "active",
      owner: null,
      lockedBy: null,
      sourceMeetingIds: [],
      collapsed: false,
    }),
  );
});

test("fromMindElixir carries suppressedNodes and schemaVersion from the previous document", () => {
  // The library knows nothing about tombstones; they must survive every save.
  const restored = fromMindElixir(toMindElixir(doc()), doc());
  expect(restored.suppressedNodes).toEqual([{ title: "Smazané téma", deletedBy: "ondra@anela.cz" }]);
  expect(restored.schemaVersion).toBe(1);
});

test("toMindElixir throws when the root id is missing rather than emitting a headless map", () => {
  const broken: MindMapDocument = { ...doc(), rootNodeId: "nope" };
  expect(() => toMindElixir(broken)).toThrow(/root/i);
});

test("toMindElixir terminates when the root lists itself as its own parent", () => {
  // Each node has exactly one parentId, so the only cycle the walk can actually
  // reach from the root is a self-parenting node: childrenByParent["root"] then
  // contains root itself, and an unguarded build() would recurse forever.
  const cyclic: MindMapDocument = {
    schemaVersion: 1,
    rootNodeId: "root",
    nodes: [node("root", "root"), node("a", "root")],
    suppressedNodes: [],
  };
  const data = toMindElixir(cyclic);
  expect(data.nodeData.id).toBe("root");
  expect(data.nodeData.children?.map((c) => c.id)).toEqual(["a"]);
}, 2000);

test("displayFieldsFor renders the owner as a tag and the lock and note as icons", () => {
  const fields = displayFieldsFor(
    { status: "active", owner: "Bára", lockedBy: "ondra@anela.cz", sourceMeetingIds: [] },
    "poznámka",
  );
  expect(fields.tags).toEqual(["Bára"]);
  expect(fields.icons).toEqual(["🔒", "📝"]);
});

test("displayFieldsFor styles idea, done and blocked distinctly", () => {
  const base = { owner: null, lockedBy: null, sourceMeetingIds: [] };
  expect(displayFieldsFor({ ...base, status: "idea" }, null).style).toEqual(
    expect.objectContaining({ border: expect.stringContaining("dashed") }),
  );
  expect(displayFieldsFor({ ...base, status: "done" }, null).style).toEqual(
    expect.objectContaining({ textDecoration: "line-through" }),
  );
  expect(displayFieldsFor({ ...base, status: "blocked" }, null).style).toEqual(
    expect.objectContaining({ border: expect.stringContaining("#EF4444") }),
  );
  // "active" needs none of the three keys, but the style object itself is still
  // present — see the next test for why that matters.
  expect(displayFieldsFor({ ...base, status: "active" }, null).style).toEqual({
    border: undefined,
    color: undefined,
    textDecoration: undefined,
  });
});

test("displayFieldsFor emits all three style keys so mind-elixir's reshapeNode merge cannot carry a stale value across a status change", () => {
  // reshapeNode (node_modules/mind-elixir/dist/MindElixir.js) merges the OLD style
  // object into the new one: `o.style && t.style && (t.style = Object.assign(o.style,
  // t.style))`. That only clears a key if the new style object actually has that key
  // — simulate exactly that merge here for the two cases the review flagged.
  const base = { owner: null, lockedBy: null, sourceMeetingIds: [] };
  const merge = (oldStyle: object, newStyle: object) => Object.assign({ ...oldStyle }, newStyle);

  const doneStyle = displayFieldsFor({ ...base, status: "done" }, null).style!;
  const ideaStyle = displayFieldsFor({ ...base, status: "idea" }, null).style!;
  expect((merge(doneStyle, ideaStyle) as Record<string, unknown>).textDecoration).toBeUndefined();

  const blockedStyle = displayFieldsFor({ ...base, status: "blocked" }, null).style!;
  const doneStyle2 = displayFieldsFor({ ...base, status: "done" }, null).style!;
  expect((merge(blockedStyle, doneStyle2) as Record<string, unknown>).border).toBeUndefined();
});
