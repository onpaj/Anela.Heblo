using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Deterministic post-processing of every LLM map update. Locks are enforced here,
/// in code — never by trusting the model. Works on clones; inputs are never mutated.
/// </summary>
public class MindMapGuard
{
    public MindMapDocument ApplyLlmUpdate(MindMapDocument previous, MindMapDocument llmResult, Guid meetingId)
    {
        if (llmResult.RootNodeId != previous.RootNodeId)
            throw new MindMapGuardException(
                $"LLM changed the root node id from '{previous.RootNodeId}' to '{llmResult.RootNodeId}'.");

        RejectUnmergeableInput(previous, llmResult);

        var result = MindMapJson.Clone(llmResult);
        var prevById = previous.Nodes.ToDictionary(n => n.Id);

        RemoveRecreatedSuppressedNodes(result, previous, prevById);
        EnforceLockedNodes(result, previous, prevById);
        AssignServerIdsToNewNodes(result, prevById, meetingId);
        MergeUiMetadata(result, prevById);

        result.SchemaVersion = previous.SchemaVersion;
        result.SuppressedNodes = previous.SuppressedNodes
            .Select(s => new SuppressedNode { Title = s.Title, DeletedBy = s.DeletedBy })
            .ToList();

        var errors = MindMapDocumentValidator.Validate(result);
        if (errors.Count > 0)
            throw new MindMapGuardException($"Guarded document failed validation: {string.Join(" ", errors)}");

        return result;
    }

    /// <summary>
    /// Rejects only the malformations that would crash the merge itself (duplicate/empty ids,
    /// null titles) — deliberately NOT a full <see cref="MindMapDocumentValidator"/> run, since a
    /// structurally invalid arriving document can legitimately become valid after the guard runs
    /// (e.g. a deleted locked node's child is repaired by re-inserting the locked parent).
    /// A null title is exempt for ids that were locked in <paramref name="previous"/>: their title
    /// is unconditionally restored by <see cref="EnforceLockedNodes"/> before the validator gate,
    /// so rejecting it here would turn a recoverable update into a false hard failure.
    /// </summary>
    private static void RejectUnmergeableInput(MindMapDocument previous, MindMapDocument llmResult)
    {
        var lockedIds = new HashSet<string>(
            previous.Nodes.Where(n => n.LockedBy != null).Select(n => n.Id));

        var seenIds = new HashSet<string>();
        foreach (var node in llmResult.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                throw new MindMapGuardException("LLM returned a node with an empty id.");
            if (!seenIds.Add(node.Id))
                throw new MindMapGuardException($"LLM returned duplicate node id '{node.Id}'.");
            if (node.Title == null && !lockedIds.Contains(node.Id))
                throw new MindMapGuardException($"LLM returned node '{node.Id}' with a null title.");
        }

        if (previous.SuppressedNodes.Any(s => s.Title == null))
            throw new MindMapGuardException("Previous document has a suppressed node with a null title.");
    }

    private static void RemoveRecreatedSuppressedNodes(
        MindMapDocument result, MindMapDocument previous, Dictionary<string, MindMapNode> prevById)
    {
        var suppressedTitles = new HashSet<string>(
            previous.SuppressedNodes.Select(s => s.Title.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var recreated = result.Nodes
            .Where(n => !prevById.ContainsKey(n.Id) && suppressedTitles.Contains(n.Title.Trim()))
            .ToList();

        foreach (var node in recreated)
        {
            foreach (var child in result.Nodes.Where(c => c.ParentId == node.Id))
                child.ParentId = node.ParentId;
            result.Nodes.Remove(node);
        }
    }

    private static void EnforceLockedNodes(
        MindMapDocument result, MindMapDocument previous, Dictionary<string, MindMapNode> prevById)
    {
        var resultById = result.Nodes.ToDictionary(n => n.Id);

        foreach (var prev in previous.Nodes.Where(n => n.LockedBy != null))
        {
            if (resultById.TryGetValue(prev.Id, out var node))
            {
                node.Title = prev.Title;
                node.Notes = prev.Notes;
                node.Owner = prev.Owner;
            }
            else
            {
                var reinserted = new MindMapNode
                {
                    Id = prev.Id,
                    ParentId = FindNearestSurvivingAncestor(prev, prevById, resultById, result.RootNodeId),
                    Title = prev.Title,
                    Notes = prev.Notes,
                    Status = prev.Status,
                    Owner = prev.Owner,
                    LockedBy = prev.LockedBy,
                    SourceMeetingIds = prev.SourceMeetingIds.ToList()
                };
                result.Nodes.Add(reinserted);
                resultById[prev.Id] = reinserted;
            }
        }
    }

    private static string FindNearestSurvivingAncestor(
        MindMapNode node,
        Dictionary<string, MindMapNode> prevById,
        Dictionary<string, MindMapNode> resultById,
        string rootId)
    {
        var parentId = node.ParentId;
        var hops = 0;
        while (parentId != null && hops++ <= prevById.Count)
        {
            if (resultById.ContainsKey(parentId)) return parentId;
            parentId = prevById.TryGetValue(parentId, out var parent) ? parent.ParentId : null;
        }
        return rootId;
    }

    private static void AssignServerIdsToNewNodes(
        MindMapDocument result, Dictionary<string, MindMapNode> prevById, Guid meetingId)
    {
        var idMap = new Dictionary<string, string>();
        foreach (var node in result.Nodes.Where(n => !prevById.ContainsKey(n.Id)))
        {
            var newId = Guid.NewGuid().ToString("N");
            idMap[node.Id] = newId;
            node.Id = newId;
            if (!node.SourceMeetingIds.Contains(meetingId))
                node.SourceMeetingIds.Add(meetingId);
        }

        foreach (var node in result.Nodes)
        {
            if (node.ParentId != null && idMap.TryGetValue(node.ParentId, out var mapped))
                node.ParentId = mapped;
        }
    }

    private static void MergeUiMetadata(MindMapDocument result, Dictionary<string, MindMapNode> prevById)
    {
        foreach (var node in result.Nodes)
        {
            if (prevById.TryGetValue(node.Id, out var prev))
            {
                node.Position = prev.Position == null
                    ? null
                    : new NodePosition { X = prev.Position.X, Y = prev.Position.Y };
                node.Collapsed = prev.Collapsed;
                node.LockedBy = prev.LockedBy;
                node.SourceMeetingIds = UnionSourceMeetingIds(prev.SourceMeetingIds, node.SourceMeetingIds);
            }
            else
            {
                node.Position = null;
                node.Collapsed = false;
                node.LockedBy = null;
            }
        }
    }

    /// <summary>
    /// The LLM may ADD provenance (attributing an existing node to the new meeting) but must never
    /// be able to drop it — so existing nodes keep the union, previous ids first, no duplicates.
    /// </summary>
    private static List<Guid> UnionSourceMeetingIds(List<Guid> previousIds, List<Guid> llmIds)
    {
        var merged = new List<Guid>(previousIds);
        foreach (var id in llmIds)
        {
            if (!merged.Contains(id))
                merged.Add(id);
        }
        return merged;
    }
}
