using Anela.Heblo.Application.Features.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Transport.UseCases.CreateNewTransportBox;

public class CreateNewTransportBoxHandler : IRequestHandler<CreateNewTransportBoxRequest, CreateNewTransportBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateNewTransportBoxHandler> _logger;
    private readonly IMapper _mapper;

    public CreateNewTransportBoxHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<CreateNewTransportBoxHandler> logger,
        IMapper mapper)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
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
                CreatorId = Guid.TryParse(currentUser.Id, out var userId) ? userId : null,
                CreationTime = DateTime.UtcNow,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                ExtraProperties = "{}"
            };

            await _repository.AddAsync(transportBox, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created new transport box with ID {BoxId} by user {UserName}", transportBox.Id, userName);

            var transportBoxDto = _mapper.Map<TransportBoxDto>(transportBox);

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
}