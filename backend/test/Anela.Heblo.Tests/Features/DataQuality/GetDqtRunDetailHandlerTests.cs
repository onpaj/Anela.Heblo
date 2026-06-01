using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class GetDqtRunDetailHandlerTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetDqtRunDetailHandler _sut;

    public GetDqtRunDetailHandlerTests()
    {
        _sut = new GetDqtRunDetailHandler(_repositoryMock.Object, _mapperMock.Object, NullLogger<GetDqtRunDetailHandler>.Instance);
    }

    [Fact]
    public async Task Handle_RunNotFound_ReturnsNotFoundError()
    {
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        var request = new GetDqtRunDetailRequest { Id = id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtRunNotFound, response.ErrorCode);
        Assert.Null(response.Run);
    }

    [Fact]
    public async Task Handle_RunExists_ReturnsMappedDetail()
    {
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual);
        var dto = new DqtRunDto { Id = run.Id };
        var resultDtos = new List<InvoiceDqtResultDto>();

        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(run.Id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _mapperMock
            .Setup(m => m.Map<DqtRunDto>(run))
            .Returns(dto);

        _mapperMock
            .Setup(m => m.Map<List<InvoiceDqtResultDto>>(run.Results))
            .Returns(resultDtos);

        var request = new GetDqtRunDetailRequest { Id = run.Id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Run);
        Assert.Equal(run.Id, response.Run.Id);
        Assert.Null(response.ErrorCode);
    }
}
