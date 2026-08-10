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
            }
            else
            {
                node.Position = null;
                node.Collapsed = false;
                node.LockedBy = null;
            }
        }
    }
}
