using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftDqtResultShaper : IDqtResultShaper
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly IMapper _mapper;

    public DriftDqtResultShaper(
        IDqtRunRepository repository,
        IEnumerable<IDriftDqtComparer> comparers,
        IMapper mapper)
    {
        _repository = repository;
        _comparers = comparers;
        _mapper = mapper;
    }

    public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);

    public async Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)
    {
        var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(run.Id, page, pageSize, ct);
        response.DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems);
        response.TotalDriftResults = driftTotal;
    }
}
