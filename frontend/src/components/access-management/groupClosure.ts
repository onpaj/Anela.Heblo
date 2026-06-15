export type GroupNode = {
  id: string;
  permissions: string[];
  parentGroupIds: string[];
};

/**
 * Cycle-safe transitive closure of group nesting → set of inherited permissions.
 *
 * TS port of backend GroupClosure.Resolve. Seeds the traversal with the selected
 * parent (included) group IDs only, so the result contains permissions inherited
 * from nested groups — never the editing group's own direct permissions.
 */
export function resolveInheritedPermissions(
  selectedParentIds: string[],
  groups: GroupNode[],
): Set<string> {
  const groupsById = new Map(groups.map((g) => [g.id, g]));

  const visited = new Set<string>();
  const queue: string[] = [...selectedParentIds];
  const result = new Set<string>();

  while (queue.length > 0) {
    const groupId = queue.shift()!;
    if (visited.has(groupId)) continue; // already processed → cycle/diamond safe
    visited.add(groupId);

    const group = groupsById.get(groupId);
    if (!group) continue;

    for (const permission of group.permissions) result.add(permission);
    for (const parentId of group.parentGroupIds) queue.push(parentId);
  }

  return result;
}
