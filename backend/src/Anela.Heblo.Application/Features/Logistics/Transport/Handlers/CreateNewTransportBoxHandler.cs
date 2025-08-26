using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class CreateNewTransportBoxHandler : IRequestHandler<CreateNewTransportBoxRequest, CreateNewTransportBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateNewTransportBoxHandler> _logger;

    public CreateNewTransportBoxHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<CreateNewTransportBoxHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<CreateNewTransportBoxResponse> Handle(CreateNewTransportBoxRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser.Name;

            var transportBox = new TransportBox
            {
                Description = request.Description,
                CreatorId = Guid.TryParse(currentUser.Id, out var userId) ? userId : null
            };

            await _repository.AddAsync(transportBox, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created new transport box with ID {BoxId} by user {UserName}", transportBox.Id, userName);

            var transportBoxDto = MapToDto(transportBox);

            return new CreateNewTransportBoxResponse
            {
                Success = true,
                TransportBox = transportBoxDto
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new transport box");
            return new CreateNewTransportBoxResponse
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
            // Audit fields
            CreationTime = transportBox.CreationTime,
            CreatorId = transportBox.CreatorId,
            LastModificationTime = transportBox.LastModificationTime,
            LastModifierId = transportBox.LastModifierId,
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