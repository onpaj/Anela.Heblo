using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtResultShaper : IDqtResultShaper
{
    private readonly IMapper _mapper;

    public InvoiceDqtResultShaper(IMapper mapper)
    {
        _mapper = mapper;
    }

    public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;

    public Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)
    {
        response.Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results);
        return Task.CompletedTask;
    }
}
