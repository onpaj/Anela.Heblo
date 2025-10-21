using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ReceiveTransportBox;

public class ReceiveTransportBoxHandler : IRequestHandler<ReceiveTransportBoxRequest, ReceiveTransportBoxResponse>
{
    private readonly ILogger<ReceiveTransportBoxHandler> _logger;
    private readonly ITransportBoxRepository _repository;

    public ReceiveTransportBoxHandler(
        ILogger<ReceiveTransportBoxHandler> logger,
        ITransportBoxRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<ReceiveTransportBoxResponse> Handle(ReceiveTransportBoxRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Receiving transport box with ID: {BoxId} by user: {UserName}",
            request.BoxId, request.UserName);

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            _logger.LogWarning("Empty user name provided for transport box receive");
            return new ReceiveTransportBoxResponse(ErrorCodes.RequiredFieldMissing,
                new Dictionary<string, string> { { "Field", "UserName" } });
        }

        var transportBox = await _repository.GetByIdWithDetailsAsync(request.BoxId);

        if (transportBox == null)
        {
            _logger.LogWarning("Transport box with ID {BoxId} not found", request.BoxId);
            return new ReceiveTransportBoxResponse(ErrorCodes.TransportBoxNotFound,
                new Dictionary<string, string> { { "BoxId", request.BoxId.ToString() } });
        }

        // Check if box is in a receivable state (Reserve or InTransit)
        if (transportBox.State != TransportBoxState.Reserve && transportBox.State != TransportBoxState.InTransit)
        {
            _logger.LogWarning("Transport box {BoxId} ({BoxCode}) is in state {State}, cannot be received",
                request.BoxId, transportBox.Code, transportBox.State);
            var response = new ReceiveTransportBoxResponse(ErrorCodes.TransportBoxStateChangeError,
                new Dictionary<string, string>
                {
                    { "BoxId", transportBox.Id.ToString() },
                    { "BoxCode", transportBox.Code ?? "" },
                    { "CurrentState", GetStateLabel(transportBox.State) }
                });
            response.BoxId = transportBox.Id;
            response.BoxCode = transportBox.Code;
            return response;
        }

        try
        {
            // Use the domain method to receive the box
            transportBox.Receive(DateTime.UtcNow, request.UserName);

            // Save the changes
            await _repository.UpdateAsync(transportBox);
            await _repository.SaveChangesAsync();

            _logger.LogInformation("Successfully received transport box {BoxId} ({BoxCode}) by user {UserName}",
                transportBox.Id, transportBox.Code, request.UserName);

            var successResponse = new ReceiveTransportBoxResponse();
            successResponse.BoxId = transportBox.Id;
            successResponse.BoxCode = transportBox.Code;
            return successResponse;
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error while receiving transport box {BoxId}: {Error}",
                request.BoxId, ex.Message);
            var validationResponse = new ReceiveTransportBoxResponse(ErrorCodes.ValidationError,
                new Dictionary<string, string>
                {
                    { "BoxId", transportBox.Id.ToString() },
                    { "ErrorMessage", ex.Message }
                });
            validationResponse.BoxId = transportBox.Id;
            validationResponse.BoxCode = transportBox.Code;
            return validationResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while receiving transport box {BoxId}", request.BoxId);
            var errorResponse = new ReceiveTransportBoxResponse(ex);
            errorResponse.BoxId = transportBox.Id;
            errorResponse.BoxCode = transportBox.Code;
            return errorResponse;
        }
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