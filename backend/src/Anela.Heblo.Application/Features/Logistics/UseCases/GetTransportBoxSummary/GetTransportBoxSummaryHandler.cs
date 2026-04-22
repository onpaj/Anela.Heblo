using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxSummary;

public class GetTransportBoxSummaryHandler : IRequestHandler<GetTransportBoxSummaryRequest, GetTransportBoxSummaryResponse>
{
    private readonly ITransportBoxRepository _repository;

    public GetTransportBoxSummaryHandler(ITransportBoxRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetTransportBoxSummaryResponse> Handle(GetTransportBoxSummaryRequest request, CancellationToken cancellationToken)
    {
        var stateSummary = await _repository.GetStateSummaryAsync(
            code: request.Code,
            productCode: request.ProductCode,
            cancellationToken: cancellationToken);

        var totalBoxes = stateSummary.Values.Sum();
        var activeBoxes = stateSummary
            .Where(kv => kv.Key != TransportBoxState.Closed)
            .Sum(kv => kv.Value);

        var stateCounts = Enum.GetValues<TransportBoxState>()
            .ToDictionary(
                state => state.ToString(),
                state => stateSummary.GetValueOrDefault(state, 0));

        return new GetTransportBoxSummaryResponse
        {
            TotalBoxes = totalBoxes,
            ActiveBoxes = activeBoxes,
            StatesCounts = stateCounts
        };
    }
}