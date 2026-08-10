using Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class RestoreMindMapVersionHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    private RestoreMindMapVersionHandler CreateSut() => new(_repository.Object);

    [Fact]
    public async Task Handle_RestoresVersionJson_AndSnapshotsCurrent()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{\"v\":\"current\"}", Status = MindMapStatus.Idle };
        map.Versions.Add(new MindMapVersion { VersionNumber = 1, Json = "{\"v\":\"old\"}" });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = map.Id, VersionNumber = 1 }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("{\"v\":\"old\"}", map.CurrentJson);
        Assert.Equal("{\"v\":\"old\"}", response.DocumentJson);
        var snapshot = map.Versions.Single(v => v.VersionNumber == 2);
        Assert.Equal("{\"v\":\"current\"}", snapshot.Json);
        Assert.Null(snapshot.TriggerMeetingId);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenMapDoesNotExist()
    {
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((MindMap?)null);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = id, VersionNumber = 1 }, CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_ForUnknownVersion()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Idle };
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = map.Id, VersionNumber = 99 }, CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhileUpdating()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Updating };
        map.Versions.Add(new MindMapVersion { VersionNumber = 1, Json = "{}" });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = map.Id, VersionNumber = 1 }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }
}
