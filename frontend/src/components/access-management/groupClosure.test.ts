import { resolveInheritedPermissions, GroupNode } from "./groupClosure";

describe("resolveInheritedPermissions", () => {
  test("returns empty set when no parent groups are selected", () => {
    const groups: GroupNode[] = [
      { id: "a", permissions: ["x.read"], parentGroupIds: [] },
    ];

    const result = resolveInheritedPermissions([], groups);

    expect(result.size).toBe(0);
  });

  test("includes direct permissions of selected parent groups", () => {
    const groups: GroupNode[] = [
      { id: "parent", permissions: ["x.read", "x.write"], parentGroupIds: [] },
    ];

    const result = resolveInheritedPermissions(["parent"], groups);

    expect(Array.from(result).sort()).toEqual(["x.read", "x.write"]);
  });

  test("walks a deep chain A→B→C and unions all permissions", () => {
    const groups: GroupNode[] = [
      { id: "a", permissions: ["a.read"], parentGroupIds: ["b"] },
      { id: "b", permissions: ["b.read"], parentGroupIds: ["c"] },
      { id: "c", permissions: ["c.read"], parentGroupIds: [] },
    ];

    const result = resolveInheritedPermissions(["a"], groups);

    expect(Array.from(result).sort()).toEqual(["a.read", "b.read", "c.read"]);
  });

  test("counts a diamond shared ancestor only once", () => {
    const groups: GroupNode[] = [
      { id: "a", permissions: ["a.read"], parentGroupIds: ["b", "c"] },
      { id: "b", permissions: ["b.read"], parentGroupIds: ["d"] },
      { id: "c", permissions: ["c.read"], parentGroupIds: ["d"] },
      { id: "d", permissions: ["d.read"], parentGroupIds: [] },
    ];

    const result = resolveInheritedPermissions(["a"], groups);

    expect(Array.from(result).sort()).toEqual([
      "a.read",
      "b.read",
      "c.read",
      "d.read",
    ]);
  });

  test("terminates safely on a cycle", () => {
    const groups: GroupNode[] = [
      { id: "a", permissions: ["a.read"], parentGroupIds: ["b"] },
      { id: "b", permissions: ["b.read"], parentGroupIds: ["a"] },
    ];

    const result = resolveInheritedPermissions(["a"], groups);

    expect(Array.from(result).sort()).toEqual(["a.read", "b.read"]);
  });

  test("excludes the editing group's own permissions (only parents are seeded)", () => {
    const groups: GroupNode[] = [
      { id: "self", permissions: ["self.read"], parentGroupIds: ["parent"] },
      { id: "parent", permissions: ["parent.read"], parentGroupIds: [] },
    ];

    // Seeded with the parent ids only, as GroupDetailPage does.
    const result = resolveInheritedPermissions(["parent"], groups);

    expect(Array.from(result)).toEqual(["parent.read"]);
    expect(result.has("self.read")).toBe(false);
  });

  test("ignores selected ids that are not present in the graph", () => {
    const groups: GroupNode[] = [
      { id: "a", permissions: ["a.read"], parentGroupIds: [] },
    ];

    const result = resolveInheritedPermissions(["missing"], groups);

    expect(result.size).toBe(0);
  });
});
