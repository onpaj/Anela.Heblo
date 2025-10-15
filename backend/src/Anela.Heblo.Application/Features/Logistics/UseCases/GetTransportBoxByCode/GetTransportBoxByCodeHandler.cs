using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;

public class GetTransportBoxByCodeHandler : IRequestHandler<GetTransportBoxByCodeRequest, GetTransportBoxByCodeResponse>
{
    private readonly ILogger<GetTransportBoxByCodeHandler> _logger;
    private readonly ITransportBoxRepository _repository;

    public GetTransportBoxByCodeHandler(
        ILogger<GetTransportBoxByCodeHandler> logger,
        ITransportBoxRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<GetTransportBoxByCodeResponse> Handle(GetTransportBoxByCodeRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting transport box with code: {BoxCode}", request.BoxCode);

        if (string.IsNullOrWhiteSpace(request.BoxCode))
        {
            _logger.LogWarning("Empty box code provided");
            return new GetTransportBoxByCodeResponse(ErrorCodes.RequiredFieldMissing,
                new Dictionary<string, string> { { "Field", "BoxCode" } });
        }

        var transportBox = await _repository.GetByCodeAsync(request.BoxCode.ToUpper());

        if (transportBox == null)
        {
            _logger.LogWarning("Transport box with code {BoxCode} not found", request.BoxCode);
            return new GetTransportBoxByCodeResponse(ErrorCodes.TransportBoxNotFound,
                new Dictionary<string, string> { { "BoxCode", request.BoxCode } });
        }

        // Check if box is in a receivable state (Reserve or InTransit)
        if (transportBox.State != TransportBoxState.Reserve && transportBox.State != TransportBoxState.InTransit)
        {
            _logger.LogWarning("Transport box {BoxCode} is in state {State}, cannot be received",
                request.BoxCode, transportBox.State);
            return new GetTransportBoxByCodeResponse(ErrorCodes.TransportBoxStateChangeError,
                new Dictionary<string, string>
                {
                    { "BoxCode", request.BoxCode },
                    { "CurrentState", GetStateLabel(transportBox.State) }
                });
        }

        // Load full details including items
        var detailedBox = await _repository.GetByIdWithDetailsAsync(transportBox.Id);
        if (detailedBox == null)
        {
            _logger.LogError("Failed to load detailed data for transport box {BoxCode}", request.BoxCode);
            return new GetTransportBoxByCodeResponse(ErrorCodes.DatabaseError,
                new Dictionary<string, string> { { "BoxCode", request.BoxCode } });
        }

        var dto = new TransportBoxDto
        {
            Id = detailedBox.Id,
            Code = detailedBox.Code,
            State = detailedBox.State.ToString(),
            DefaultReceiveState = detailedBox.DefaultReceiveState.ToString(),
            Description = detailedBox.Description,
            LastStateChanged = detailedBox.LastStateChanged,
            Location = detailedBox.Location,
            IsInTransit = detailedBox.IsInTransit,
            IsInReserve = detailedBox.IsInReserve,
            ItemCount = detailedBox.Items.Count,
            Items = detailedBox.Items.Select(item => new TransportBoxItemDto
            {
                Id = item.Id,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Amount = item.Amount,
                DateAdded = item.DateAdded,
                UserAdded = item.UserAdded
            }).ToList(),
            StateLog = detailedBox.StateLog.Select(log => new TransportBoxStateLogDto
            {
                Id = log.Id,
                State = log.State.ToString(),
                StateDate = log.StateDate,
                User = log.User,
                Description = log.Description
            }).OrderByDescending(log => log.StateDate).ToList(),
            AllowedTransitions = detailedBox.TransitionNode.GetAllTransitions().Select(transition => new TransportBoxTransitionDto
            {
                NewState = transition.NewState.ToString(),
                TransitionType = transition.TransitionType.ToString(),
                SystemOnly = transition.SystemOnly,
                Label = GetStateLabel(transition.NewState)
            }).ToList()
        };

        _logger.LogInformation("Retrieved transport box {BoxCode} with {ItemCount} items in {State} state",
            detailedBox.Code, detailedBox.Items.Count, detailedBox.State);

        return new GetTransportBoxByCodeResponse { TransportBox = dto };
    }

    private static string GetStateLabel(TransportBoxState state)
    {
        return state switch
        {
            TransportBoxState.New => "Nový",
            TransportBoxState.Opened => "Otevřený",
            TransportBoxState.InTransit => "V přepravě",
            TransportBoxState.Received => "Přijatý",
            TransportBoxState.Stocked => "Naskladněný",
            TransportBoxState.Reserve => "V rezervě",
            TransportBoxState.Closed => "Uzavřený",
            TransportBoxState.Error => "Chyba",
            _ => state.ToString()
        };
    }
}