using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class GetAllowedTransitionsHandler : IRequestHandler<GetAllowedTransitionsRequest, GetAllowedTransitionsResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ILogger<GetAllowedTransitionsHandler> _logger;

    // State labels mapping
    private readonly Dictionary<TransportBoxState, string> _stateLabels = new()
    {
        { TransportBoxState.New, "Nový" },
        { TransportBoxState.Opened, "Otevřený" },
        { TransportBoxState.InTransit, "V přepravě" },
        { TransportBoxState.Received, "Přijatý" },
        { TransportBoxState.InSwap, "Swap" },
        { TransportBoxState.Stocked, "Naskladněný" },
        { TransportBoxState.Reserve, "V rezervě" },
        { TransportBoxState.Closed, "Uzavřený" },
        { TransportBoxState.Error, "Chyba" }
    };

    public GetAllowedTransitionsHandler(
        ITransportBoxRepository repository,
        ILogger<GetAllowedTransitionsHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetAllowedTransitionsResponse> Handle(GetAllowedTransitionsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var box = await _repository.GetByIdAsync(request.BoxId);
            if (box == null)
            {
                return new GetAllowedTransitionsResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found."
                };
            }

            var allowedTransitions = new List<AllowedTransition>();
            var currentTransitionNode = box.TransitionNode;

            // Add next state if available
            if (currentTransitionNode.NextState != null)
            {
                var nextState = currentTransitionNode.NextState.NewState;
                var canTransition = currentTransitionNode.NextState.Condition?.Invoke(box) ?? true;
                var conditionDescription = GetConditionDescription(box.State, nextState, box);

                allowedTransitions.Add(new AllowedTransition
                {
                    State = nextState.ToString(),
                    Label = _stateLabels[nextState],
                    RequiresCondition = currentTransitionNode.NextState.Condition != null,
                    ConditionDescription = canTransition ? null : conditionDescription
                });
            }

            // Add previous state if available
            if (currentTransitionNode.PreviousState != null)
            {
                var prevState = currentTransitionNode.PreviousState.NewState;
                var canTransition = currentTransitionNode.PreviousState.Condition?.Invoke(box) ?? true;
                var conditionDescription = GetConditionDescription(box.State, prevState, box);

                allowedTransitions.Add(new AllowedTransition
                {
                    State = prevState.ToString(),
                    Label = _stateLabels[prevState],
                    RequiresCondition = currentTransitionNode.PreviousState.Condition != null,
                    ConditionDescription = canTransition ? null : conditionDescription
                });
            }

            _logger.LogInformation("Retrieved {Count} allowed transitions for transport box {BoxId} in state {CurrentState}", 
                allowedTransitions.Count, request.BoxId, box.State);

            return new GetAllowedTransitionsResponse
            {
                Success = true,
                CurrentState = box.State.ToString(),
                AllowedTransitions = allowedTransitions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting allowed transitions for transport box {BoxId}", request.BoxId);
            return new GetAllowedTransitionsResponse
            {
                Success = false,
                ErrorMessage = "An error occurred while getting allowed transitions."
            };
        }
    }

    private string? GetConditionDescription(TransportBoxState currentState, TransportBoxState targetState, TransportBox box)
    {
        return (currentState, targetState) switch
        {
            (TransportBoxState.New, TransportBoxState.Opened) when box.Code == null => "Vyžaduje přiřazení čísla boxu",
            (TransportBoxState.Opened, TransportBoxState.InTransit) when !box.Items.Any() => "Box musí obsahovat alespoň jednu položku",
            (TransportBoxState.InTransit, TransportBoxState.Opened) when box.Code == null => "Vyžaduje přiřazení čísla boxu",
            (TransportBoxState.Reserve, TransportBoxState.Opened) when box.Code == null => "Vyžaduje přiřazení čísla boxu",
            _ => null
        };
    }
}