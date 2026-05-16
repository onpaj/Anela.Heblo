using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Pipeline;

public class LeafletGenerationPersistenceBehaviorTests
{
    private readonly Mock<ILeafletGenerationRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<LeafletGenerationPersistenceBehavior>> _logger = new();

    private LeafletGenerationPersistenceBehavior CreateBehavior() =>
        new(_repository.Object, _userService.Object, _logger.Object);

    private static GenerateLeafletRequest MakeRequest() =>
        new()
        {
            Topic = "Vitamin C serum",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Medium,
        };

    private static GenerateLeafletResponse MakeResponse() =>
        new()
        {
            Content = "# Vitamin C serum\n\nGreat for skin.",
            KbSourceCount = 3,
            LeafletSourceCount = 1,
        };

    [Fact]
    public async Task Handle_SavesGenerationRow_AndSetsResponseId()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", null, true));

        LeafletGeneration? captured = null;
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletGeneration, CancellationToken>((g, _) => captured = g)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var response = MakeResponse();
        var result = await behavior.Handle(MakeRequest(), () => Task.FromResult(response), default);

        Assert.NotNull(captured);
        Assert.Equal("Vitamin C serum", captured.Topic);
        Assert.Equal("EndConsumer", captured.Audience);
        Assert.Equal("Medium", captured.Length);
        Assert.Equal("# Vitamin C serum\n\nGreat for skin.", captured.FinalMarkdown);
        Assert.Equal(3, captured.KbSourceCount);
        Assert.Equal(1, captured.LeafletSourceCount);
        Assert.Equal("user-1", captured.UserId);
        Assert.True(captured.DurationMs >= 0);
        Assert.NotEqual(Guid.Empty, captured.Id);
        Assert.Equal(captured.Id, result.Id);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", null, true));
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB down"));

        var behavior = CreateBehavior();
        var response = MakeResponse();
        var result = await behavior.Handle(MakeRequest(), () => Task.FromResult(response), default);

        Assert.Equal("# Vitamin C serum\n\nGreat for skin.", result.Content);
        Assert.Null(result.Id);
    }

    [Fact]
    public async Task Handle_ReturnsOriginalResponse()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", null, true));
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var response = MakeResponse();
        var result = await behavior.Handle(MakeRequest(), () => Task.FromResult(response), default);

        Assert.Equal(response, result);
        Assert.Equal("# Vitamin C serum\n\nGreat for skin.", result.Content);
    }
}
