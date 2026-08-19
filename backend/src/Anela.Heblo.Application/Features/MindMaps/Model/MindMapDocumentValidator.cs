namespace Anela.Heblo.Application.Features.MindMaps.Model;

public static class MindMapDocumentValidator
{
    public static List<string> Validate(MindMapDocument doc)
    {
        var errors = new List<string>();
        if (doc.Nodes is not { Count: > 0 })
        {
            errors.Add("Document has no nodes.");
            return errors;
        }

        var ids = new HashSet<string>();
        foreach (var node in doc.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                errors.Add("Node with empty id.");
            else if (!ids.Add(node.Id))
                errors.Add($"Duplicate node id '{node.Id}'.");
            if (string.IsNullOrWhiteSpace(node.Title))
                errors.Add($"Node '{node.Id}' has an empty title.");
            if (!MindMapNodeStatus.All.Contains(node.Status))
                errors.Add($"Node '{node.Id}' has unknown status '{node.Status}'.");
        }
        if (errors.Count > 0) return errors;

        var roots = doc.Nodes.Where(n => n.ParentId == null).ToList();
        if (roots.Count != 1)
            errors.Add($"Expected exactly one root node, found {roots.Count}.");
        else if (roots[0].Id != doc.RootNodeId)
            errors.Add($"RootNodeId '{doc.RootNodeId}' does not match the parentless node '{roots[0].Id}'.");

        foreach (var node in doc.Nodes.Where(n => n.ParentId != null))
        {
            if (!ids.Contains(node.ParentId!))
                errors.Add($"Node '{node.Id}' references missing parent '{node.ParentId}'.");
        }
        if (errors.Count > 0) return errors;

        var byId = doc.Nodes.ToDictionary(n => n.Id);
        foreach (var node in doc.Nodes)
        {
            var seen = new HashSet<string>();
            var current = node;
            while (current.ParentId != null)
            {
                if (!seen.Add(current.Id))
                {
                    errors.Add($"Cycle detected at node '{node.Id}'.");
                    break;
                }
                current = byId[current.ParentId];
            }
        }
        return errors;
    }
}
