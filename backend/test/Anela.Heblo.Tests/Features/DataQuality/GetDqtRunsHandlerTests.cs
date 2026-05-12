using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRuns;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class GetDqtRunsHandlerTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetDqtRunsHandler _sut;

    public GetDqtRunsHandlerTests()
    {
        _sut = new GetDqtRunsHandler(_repositoryMock.Object, _mapperMock.Object, NullLogger<GetDqtRunsHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsPagedRuns()
    {
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual);
        var runs = new List<DqtRun> { run };
        var dtos = new List<DqtRunDto> { new DqtRunDto { Id = run.Id } };

        _repositoryMock
            .Setup(r => r.GetPaginatedAsync(null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((runs, 1));

        _mapperMock
            .Setup(m => m.Map<List<DqtRunDto>>(runs))
            .Returns(dtos);

        var request = new GetDqtRunsRequest { PageNumber = 1, PageSize = 20 };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(response.Items);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsSuccessWithEmptyList()
    {
        var emptyRuns = new List<DqtRun>();
        var emptyDtos = new List<DqtRunDto>();

        _repositoryMock
            .Setup(r => r.GetPaginatedAsync(null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((emptyRuns, 0));

        _mapperMock
            .Setup(m => m.Map<List<DqtRunDto>>(emptyRuns))
            .Returns(emptyDtos);

        var request = new GetDqtRunsRequest { PageNumber = 1, PageSize = 20 };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Items);
        Assert.Null(response.ErrorCode);
    }
}
