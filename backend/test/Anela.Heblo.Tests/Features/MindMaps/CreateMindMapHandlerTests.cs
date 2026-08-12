using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;
using Anela.Heblo.Domain.Features.MindMaps;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class CreateMindMapHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    [Fact]
    public async Task Handle_CreatesMapWithSingleRootNodeNamedAfterMap()
    {
        MindMap? saved = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<MindMap>(), It.IsAny<CancellationToken>()))
            .Callback<MindMap, CancellationToken>((m, _) => saved = m);
        var handler = new CreateMindMapHandler(_repository.Object, NullLogger<CreateMindMapHandler>.Instance);

        var response = await handler.Handle(
            new CreateMindMapRequest { Name = "Web relaunch", Description = "popis" },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(saved);
        Assert.Equal("Web relaunch", saved!.Name);
        Assert.Equal(MindMapStatus.Idle, saved.Status);
        var doc = MindMapJson.Deserialize(saved.CurrentJson);
        var root = Assert.Single(doc.Nodes);
        Assert.Equal(doc.RootNodeId, root.Id);
        Assert.Equal("Web relaunch", root.Title);
        Assert.Null(root.ParentId);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
