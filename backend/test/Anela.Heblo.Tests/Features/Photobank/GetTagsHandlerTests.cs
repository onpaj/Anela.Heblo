using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetTags;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetTagsHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();

    private GetTagsHandler CreateHandler() =>
        new(_repo.Object, _cache.Object, NullLogger<GetTagsHandler>.Instance);

    [Fact]
    public async Task Handle_OnCacheHit_DoesNotCallRepository()
    {
        IReadOnlyList<TagWithCountDto>? cached = new List<TagWithCountDto>
        {
            new() { Id = 1, Name = "summer", Count = 10 },
        };
        _cache.Setup(c => c.TryGet(out cached)).Returns(true);

        var response = await CreateHandler().Handle(new GetTagsRequest(), CancellationToken.None);

        response.Tags.Should().HaveCount(1);
        response.Tags[0].Name.Should().Be("summer");
        _repo.Verify(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OnCacheMiss_CallsRepositoryAndStoresInCache()
    {
        IReadOnlyList<TagWithCountDto>? cached = null;
        _cache.Setup(c => c.TryGet(out cached)).Returns(false);

        var fromDb = new List<TagCount>
        {
            new(1, "summer", 10),
            new(2, "winter", 3),
        };
        _repo.Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(fromDb);

        var response = await CreateHandler().Handle(new GetTagsRequest(), CancellationToken.None);

        response.Tags.Should().HaveCount(2);
        response.Tags[0].Should().BeEquivalentTo(new { Id = 1, Name = "summer", Count = 10 });
        _cache.Verify(c => c.Set(It.Is<IReadOnlyList<TagWithCountDto>>(list =>
            list.Count == 2 && list[0].Name == "summer")), Times.Once);
    }

    [Fact]
    public async Task Handle_OnCacheMiss_ProducesResponseTagsAsTagWithCountDto()
    {
        IReadOnlyList<TagWithCountDto>? cached = null;
        _cache.Setup(c => c.TryGet(out cached)).Returns(false);
        _repo.Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<TagCount> { new(7, "products", 1201) });

        var response = await CreateHandler().Handle(new GetTagsRequest(), CancellationToken.None);

        response.Tags.Should().AllBeOfType<TagWithCountDto>();
        response.Tags.Should().ContainSingle(t => t.Id == 7 && t.Name == "products" && t.Count == 1201);
    }
}
