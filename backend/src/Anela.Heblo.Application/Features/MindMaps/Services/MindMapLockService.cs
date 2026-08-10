using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Applies a user-submitted document over the current one: content edits auto-lock
/// the node, deletions become tombstones, and locks/provenance can never be set
/// or cleared by the client. Assumes the caller already validated the submitted
/// document and that the root id is unchanged.
/// </summary>
public class MindMapLockService
{
    public MindMapDocument ApplyUserEdit(MindMapDocument current, MindMapDocument submitted, string userEmail)
    {
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

        var tombstones = current.Nodes
            .Where(n => !submittedIds.Contains(n.Id))
            .Select(n => new SuppressedNode { Title = n.Title, DeletedBy = userEmail });

        result.SuppressedNodes = current.SuppressedNodes
            .Select(s => new SuppressedNode { Title = s.Title, DeletedBy = s.DeletedBy })
            .Concat(tombstones)
            .ToList();
        result.SchemaVersion = current.SchemaVersion;
        return result;
    }
}
