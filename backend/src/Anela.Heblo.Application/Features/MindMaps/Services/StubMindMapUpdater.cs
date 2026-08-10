using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Deterministic updater used on staging/E2E (MindMaps:UseStubUpdater=true):
/// adds one "Porada: &lt;subject&gt;" node under the root, nothing else.
/// </summary>
public class StubMindMapUpdater : IMindMapUpdater
{
    public Task<MindMapDocument> UpdateAsync(
        MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default)
    {
        var result = MindMapJson.Clone(current);
        result.Nodes.Add(new MindMapNode
        {
            Id = $"new-{result.Nodes.Count}",
            ParentId = result.RootNodeId,
            Title = $"Porada: {meeting.Subject}",
            Status = MindMapNodeStatus.Idea
        });
        return Task.FromResult(result);
    }
}
