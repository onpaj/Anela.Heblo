namespace Anela.Heblo.Application.Features.Authorization;

/// <summary>Detects whether assigning parents to a group would create a cycle in the nesting DAG.</summary>
public static class GroupCycleCheck
{
    /// <param name="groupId">The group being edited.</param>
    /// <param name="proposedParentIds">Parent group ids we want to assign to groupId.</param>
    /// <param name="existingParents">Map of groupId → its current parent ids (excluding the edited group's edges).</param>
    public static bool WouldCreateCycle(
        Guid groupId,
        IEnumerable<Guid> proposedParentIds,
        IReadOnlyDictionary<Guid, List<Guid>> existingParents)
    {
        foreach (var parent in proposedParentIds)
        {
            if (parent == groupId) return true; // self-parent

            // Walk up from `parent`; if we reach `groupId`, the new edge closes a cycle.
            var visited = new HashSet<Guid>();
            var stack = new Stack<Guid>();
            stack.Push(parent);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == groupId) return true;
                if (!visited.Add(current)) continue;
                if (existingParents.TryGetValue(current, out var grandparents))
                    foreach (var gp in grandparents) stack.Push(gp);
            }
        }
        return false;
    }
}
