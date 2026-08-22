using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Applies a user-submitted document over the current one: content edits auto-lock
/// the node, deletions become tombstones, and locks/provenance can never be set
/// or cleared by the client. Assumes the caller already validated the submitted
/// document and that the root id is unchanged.
/// Lock and provenance survival are scoped to nodes whose id is stable across the
/// save: a submitted node bearing an id not present in <c>current</c> is always
/// treated as newly added (fresh id, fresh lock, empty provenance), even if it is
/// byte-identical to an existing node under a different id. Diffing is by id only —
/// there is no title-based adoption of a changed id onto an existing node's history.
/// </summary>
public class MindMapLockService
{
    public MindMapDocument ApplyUserEdit(MindMapDocument current, MindMapDocument submitted, string userEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);

        var result = MindMapJson.Clone(submitted);
        var currentById = current.Nodes.ToDictionary(n => n.Id);
        var submittedIds = new HashSet<string>(result.Nodes.Select(n => n.Id));

        var idMap = new Dictionary<string, string>();
        foreach (var node in result.Nodes)
        {
            if (currentById.TryGetValue(node.Id, out var existing))
            {
                var contentChanged = node.Title != existing.Title
                    || node.Notes != existing.Notes
                    || node.Owner != existing.Owner;
                node.LockedBy = contentChanged ? userEmail : existing.LockedBy;
                node.SourceMeetingIds = existing.SourceMeetingIds.ToList();
            }
            else
            {
                var newId = Guid.NewGuid().ToString("N");
                idMap[node.Id] = newId;
                node.Id = newId;
                node.LockedBy = userEmail;
                node.SourceMeetingIds = new List<Guid>();
            }
        }

        foreach (var node in result.Nodes)
        {
            if (node.ParentId != null && idMap.TryGetValue(node.ParentId, out var mapped))
                node.ParentId = mapped;
        }

        var suppressedNodes = current.SuppressedNodes
            .Select(s => new SuppressedNode { Title = s.Title, DeletedBy = s.DeletedBy })
            .ToList();
        var seenTitles = new HashSet<string>(
            suppressedNodes.Select(s => s.Title.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in current.Nodes.Where(n => !submittedIds.Contains(n.Id)))
        {
            if (seenTitles.Add(node.Title.Trim()))
                suppressedNodes.Add(new SuppressedNode { Title = node.Title, DeletedBy = userEmail });
        }

        result.SuppressedNodes = suppressedNodes;
        result.SchemaVersion = current.SchemaVersion;
        return result;
    }
}
