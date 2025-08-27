using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class AddItemToBoxHandler : IRequestHandler<AddItemToBoxRequest, AddItemToBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AddItemToBoxHandler> _logger;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public AddItemToBoxHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<AddItemToBoxHandler> logger,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<AddItemToBoxResponse> Handle(AddItemToBoxRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser.Name;

            var transportBox = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (transportBox == null)
            {
                return new AddItemToBoxResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found"
                };
            }

            var addedItem = transportBox.AddItem(
                request.ProductCode,
                request.ProductName,
                request.Amount,
                _timeProvider.GetUtcNow().UtcDateTime,
                userName);


            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added item {ProductCode} (amount: {Amount}) to transport box {BoxId} by user {UserName}",
                request.ProductCode, request.Amount, request.BoxId, userName);

            var itemDto = _mapper.Map<TransportBoxItemDto>(addedItem);
            var transportBoxDto = _mapper.Map<TransportBoxDto>(transportBox);

            return new AddItemToBoxResponse
            {
                Success = true,
                Item = itemDto,
                TransportBox = transportBoxDto
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error adding item to transport box {BoxId}: {Error}",
                request.BoxId, ex.Message);

            return new AddItemToBoxResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item {ProductCode} to transport box {BoxId}",
                request.ProductCode, request.BoxId);

            return new AddItemToBoxResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}