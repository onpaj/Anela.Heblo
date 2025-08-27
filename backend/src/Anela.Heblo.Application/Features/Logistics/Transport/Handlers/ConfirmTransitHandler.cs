using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class ConfirmTransitHandler : IRequestHandler<ConfirmTransitRequest, ConfirmTransitResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ConfirmTransitHandler> _logger;

    public ConfirmTransitHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<ConfirmTransitHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ConfirmTransitResponse> Handle(ConfirmTransitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser.Name;

            var transportBox = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (transportBox == null)
            {
                return new ConfirmTransitResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found"
                };
            }

            transportBox.ConfirmTransit(request.ConfirmationBoxNumber, DateTime.UtcNow, userName);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Confirmed transit for transport box {BoxId} with box number {BoxNumber} by user {UserName}",
                request.BoxId, request.ConfirmationBoxNumber, userName);

            var transportBoxDto = MapToDto(transportBox);

            return new ConfirmTransitResponse
            {
                Success = true,
                TransportBox = transportBoxDto
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error confirming transit for transport box {BoxId}: {Error}",
                request.BoxId, ex.Message);

            return new ConfirmTransitResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming transit for transport box {BoxId}", request.BoxId);

            return new ConfirmTransitResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static TransportBoxDto MapToDto(TransportBox transportBox)
    {
        return new TransportBoxDto
        {
            Id = transportBox.Id,
            Code = transportBox.Code,
            State = transportBox.State.ToString(),
            DefaultReceiveState = transportBox.DefaultReceiveState.ToString(),
            Description = transportBox.Description,
            LastStateChanged = transportBox.LastStateChanged,
            Location = transportBox.Location,
            IsInTransit = transportBox.IsInTransit,
            IsInReserve = transportBox.IsInReserve,
            ItemCount = transportBox.Items.Count,
            Items = transportBox.Items.Select(item => new TransportBoxItemDto
            {
                Id = item.Id,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Amount = item.Amount,
                DateAdded = item.DateAdded,
                UserAdded = item.UserAdded
            }).ToList(),
            StateLog = transportBox.StateLog.Select(log => new TransportBoxStateLogDto
            {
                Id = log.Id,
                State = log.State.ToString(),
                StateDate = log.StateDate,
                User = log.User,
                Description = log.Description
            }).ToList()
        };
    }
}