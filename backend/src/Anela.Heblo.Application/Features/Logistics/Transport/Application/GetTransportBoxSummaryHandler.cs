using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Application;

public class GetTransportBoxSummaryHandler : IRequestHandler<GetTransportBoxSummaryRequest, GetTransportBoxSummaryResponse>
{
    private readonly ITransportBoxRepository _repository;

    public GetTransportBoxSummaryHandler(ITransportBoxRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetTransportBoxSummaryResponse> Handle(GetTransportBoxSummaryRequest request, CancellationToken cancellationToken)
    {
        var result = await _repository.GetPagedListAsync(
            skip: 0,
            take: int.MaxValue, // Get all for summary
            code: request.Code,
            state: null, // Don't filter by state for summary
            fromDate: request.FromDate,
            toDate: request.ToDate,
            sortBy: null,
            sortDescending: false
        );

        var allBoxes = result.items;
        var totalBoxes = allBoxes.Count;
        var activeBoxes = allBoxes.Count(b => b.State != TransportBoxState.Closed);

        var stateCounts = new Dictionary<string, int>();
        
        // Count boxes by state
        foreach (var state in Enum.GetValues<TransportBoxState>())
        {
            var count = allBoxes.Count(b => b.State == state);
            stateCounts[state.ToString()] = count;
        }

        return new GetTransportBoxSummaryResponse
        {
            TotalBoxes = totalBoxes,
            ActiveBoxes = activeBoxes,
            StatesCounts = stateCounts
        };
    }
}