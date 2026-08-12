import { parseDocument } from "../mindMapDocument";

test("parseDocument throws on malformed json", () => {
  expect(() => parseDocument("not json")).toThrow();
});

test("parseDocument throws when rootNodeId names no node, instead of letting toMindElixir throw later", () => {
  const json = JSON.stringify({
    schemaVersion: 1,
    rootNodeId: "nope",
    nodes: [{ id: "a", parentId: null, title: "A" }],
    suppressedNodes: [],
  });
  expect(() => parseDocument(json)).toThrow();
});
