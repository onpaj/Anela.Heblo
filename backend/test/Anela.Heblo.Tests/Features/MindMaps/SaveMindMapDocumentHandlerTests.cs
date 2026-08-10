using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class SaveMindMapDocumentHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public SaveMindMapDocumentHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Ondra", "ondra@anela.cz", true));
    }

    private SaveMindMapDocumentHandler CreateSut() =>
        new(_repository.Object, new MindMapLockService(), _currentUserService.Object);

    private static MindMap MapWithDoc(out MindMapDocument doc)
    {
        doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode>
            {
                new() { Id = "root", Title = "Projekt" },
                new() { Id = "a", ParentId = "root", Title = "Větev" }
            }
        };
        return new MindMap
        {
            Id = Guid.NewGuid(),
            Name = "Projekt",
            CurrentJson = MindMapJson.Serialize(doc),
            Status = MindMapStatus.Idle
        };
    }

    [Fact]
    public async Task Handle_LocksEditedNode_AndReturnsCanonicalJson()
    {
        var map = MapWithDoc(out var doc);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var submitted = MindMapJson.Clone(doc);
        submitted.Nodes.Single(n => n.Id == "a").Title = "Přejmenováno";

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id,
            DocumentJson = MindMapJson.Serialize(submitted)
        }, CancellationToken.None);

        Assert.True(response.Success);
        var saved = MindMapJson.Deserialize(map.CurrentJson);
        Assert.Equal("ondra@anela.cz", saved.Nodes.Single(n => n.Id == "a").LockedBy);
        Assert.Equal(map.CurrentJson, response.DocumentJson);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhileUpdating()
    {
        var map = MapWithDoc(out _);
        map.Status = MindMapStatus.Updating;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id,
            DocumentJson = map.CurrentJson
        }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidDocument_OnMalformedJson()
    {
        var map = MapWithDoc(out _);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id,
            DocumentJson = "not json"
        }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapInvalidDocument, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidDocument_WhenRootChanged()
    {
        var map = MapWithDoc(out var doc);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var submitted = MindMapJson.Clone(doc);
        submitted.RootNodeId = "a";
        submitted.Nodes.Single(n => n.Id == "root").ParentId = "a";
        submitted.Nodes.Single(n => n.Id == "a").ParentId = null;

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id,
            DocumentJson = MindMapJson.Serialize(submitted)
        }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapInvalidDocument, response.ErrorCode);
    }
}
