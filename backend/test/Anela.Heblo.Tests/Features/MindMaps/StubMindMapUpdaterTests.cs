using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class StubMindMapUpdaterTests
{
    [Fact]
    public async Task UpdateAsync_AddsOneDeterministicNodeUnderRoot()
    {
        var current = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Projekt" } }
        };
        var meeting = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = "r",
            Subject = "Týmová porada",
            Summary = "s",
            RawTranscript = "t"
        };

        var result = await new StubMindMapUpdater().UpdateAsync(current, meeting);

        var added = result.Nodes.Single(n => n.Id != "root");
        Assert.Equal("root", added.ParentId);
        Assert.Equal("Porada: Týmová porada", added.Title);
        Assert.Equal(MindMapNodeStatus.Idea, added.Status);
        Assert.Single(current.Nodes); // input untouched
    }
}
