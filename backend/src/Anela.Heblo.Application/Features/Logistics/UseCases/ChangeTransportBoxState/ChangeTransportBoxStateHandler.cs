using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ChangeTransportBoxStateHandler : IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStockUpProcessingService _stockUpProcessingService;
    private readonly TimeProvider _timeProvider;


    private static readonly
        Dictionary<Tuple<TransportBoxState, TransportBoxState>,
            Func<ChangeTransportBoxStateHandler, Func<TransportBox, ChangeTransportBoxStateRequest, CancellationToken, Task<ChangeTransportBoxStateResponse?>>>> CallBackMap = new()
        {
            { new Tuple<TransportBoxState, TransportBoxState>(TransportBoxState.New, TransportBoxState.Opened), h => h.HandleNewToOpened },
            { new Tuple<TransportBoxState, TransportBoxState>(TransportBoxState.Opened, TransportBoxState.Reserve), h => h.HandleOpenToReserve },
            { new Tuple<TransportBoxState, TransportBoxState>(TransportBoxState.InTransit, TransportBoxState.Received), h => h.HandleReceived },
            { new Tuple<TransportBoxState, TransportBoxState>(TransportBoxState.Reserve, TransportBoxState.Received), h => h.HandleReceived },
        };

    


    public ChangeTransportBoxStateHandler(
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<ChangeTransportBoxStateHandler> logger,
        ICurrentUserService currentUserService,
        IStockUpProcessingService stockUpProcessingService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
        _stockUpProcessingService = stockUpProcessingService;
        _timeProvider = timeProvider;


    }

    public async Task<ChangeTransportBoxStateResponse> Handle(ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var box = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (box == null)
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.TransportBoxNotFound,
                    Params = new Dictionary<string, string>() { { nameof(request.BoxId), request.BoxId.ToString() } },
                };
            }
            
            box.AssignBoxCodeIfAny(request.BoxCode);
            box.AssignLocationIfAny(request.Location);

            // Get the transition action
            var transition = box.TransitionNode.GetTransition(request.NewState);

            


            // Check condition if exists
            if (transition.Condition != null && !transition.Condition(box))
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.TransportBoxStateChangeError,
                    Params = new Dictionary<string, string> { { "state", request.NewState.ToString() } }
                };
            }
            
        

            // Set location if provided (typically for Reserve state)
            if (!string.IsNullOrEmpty(request.Location))
            {
                box.Location = request.Location;
            }

            // Set description if provided
            if (!string.IsNullOrEmpty(request.Description))
            {
                box.Description = request.Description;
            }

           
            if(CallBackMap.TryGetValue(new Tuple<TransportBoxState, TransportBoxState>(box.State, request.NewState), out var callbackFactory))
            {
                var callback = callbackFactory(this);
                var callbackResult = await callback(box, request, cancellationToken);
                if (callbackResult != null)
                {
                    return callbackResult;
                }
            }
            
            // Execute the transition
            var currentUser = _currentUserService.GetCurrentUser();
            var currentTime = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
            var userName = currentUser.IsAuthenticated ? currentUser.Name ?? "Unknown User" : "Anonymous";

            await transition.ChangeStateAsync(box, currentTime, userName);

            // Save changes
            await _repository.UpdateAsync(box, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Get updated box details
            var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
            var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);

            _logger.LogInformation("Transport box {BoxId} state changed to {NewState}", request.BoxId, request.NewState);

            return new ChangeTransportBoxStateResponse
            {
                Success = true,
                UpdatedBox = updatedBox
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("State transition validation failed for box {BoxId}: {Message}", request.BoxId, ex.Message);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "details", ex.Message } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing state for transport box {BoxId}", request.BoxId);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxStateChangeError,
                Params = new Dictionary<string, string> { { "boxId", request.BoxId.ToString() } }
            };
        }
    }
    
    private async Task<ChangeTransportBoxStateResponse?> HandleNewToOpened(TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.BoxCode))
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "BoxCode" } }
            };
        }

        // Check if another active box with the same code already exists
        var normalizedCode = request.BoxCode.ToUpper();
        var isCodeActive = await _repository.IsBoxCodeActiveAsync(normalizedCode);
        if (isCodeActive)
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                Params = new Dictionary<string, string> { { "code", normalizedCode } }
            };
        }

        // Close all stocked boxes
        var (stocked, _) = await _repository.GetPagedListAsync(skip: 0, take: 0, code: request.BoxCode, state: TransportBoxState.Stocked);
        foreach (var s in stocked)
        {
            s.Close(_timeProvider.GetUtcNow().UtcDateTime, _currentUserService.GetCurrentUser().Name ?? "System");
            await _repository.UpdateAsync(s, cancellationToken);
        }

        return null;
    }
    
    private async Task<ChangeTransportBoxStateResponse?> HandleOpenToReserve(TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Location))
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "Location" } }
            };
        }

        return null;
    }
    
    private async Task<ChangeTransportBoxStateResponse?> HandleReceived(TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        foreach (var item in box.Items)
        {
            var documentNumber = $"BOX-{box.Id:000000}-{item.ProductCode}";

            await _stockUpProcessingService.CreateOperationAsync(
                documentNumber,
                item.ProductCode,
                (int)item.Amount,
                StockUpSourceType.TransportBox,
                box.Id,
                cancellationToken);

            _logger.LogDebug("Created StockUpOperation {DocumentNumber} for product {ProductCode}, amount {Amount}",
                documentNumber, item.ProductCode, item.Amount);
        }

        _logger.LogInformation("Successfully created {Count} StockUpOperations for box {BoxId} ({BoxCode})",
            box.Items.Count, box.Id, box.Code);

        return null;
    }
}