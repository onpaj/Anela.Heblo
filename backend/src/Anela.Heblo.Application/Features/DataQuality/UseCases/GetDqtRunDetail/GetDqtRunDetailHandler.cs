using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;

public class GetDqtRunDetailHandler : IRequestHandler<GetDqtRunDetailRequest, GetDqtRunDetailResponse>
{
    private readonly IDqtRunRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetDqtRunDetailHandler> _logger;

    public GetDqtRunDetailHandler(IDqtRunRepository repository, IMapper mapper, ILogger<GetDqtRunDetailHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GetDqtRunDetailResponse> Handle(GetDqtRunDetailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _repository.GetWithResultsAsync(request.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

            if (run == null)
            {
                return new GetDqtRunDetailResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.DqtRunNotFound
                };
            }

            if (run.TestType == DqtTestType.IssuedInvoiceComparison)
            {
                return new GetDqtRunDetailResponse
                {
                    Success = true,
                    Run = _mapper.Map<DqtRunDto>(run),
                    Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
                };
            }

            var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
                run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

            return new GetDqtRunDetailResponse
            {
                Success = true,
                Run = _mapper.Map<DqtRunDto>(run),
                DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
                TotalDriftResults = driftTotal
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
            return new GetDqtRunDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception
            };
        }
    }
}
