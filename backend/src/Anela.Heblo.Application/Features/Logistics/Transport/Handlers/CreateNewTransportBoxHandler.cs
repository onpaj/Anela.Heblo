using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Infrastructure;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class CreateNewTransportBoxHandler : IRequestHandler<CreateNewTransportBoxRequest, CreateNewTransportBoxResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateNewTransportBoxHandler> _logger;
    private readonly IMapper _mapper;

    public CreateNewTransportBoxHandler(
        IUnitOfWork unitOfWork,
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<CreateNewTransportBoxHandler> logger,
        IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<CreateNewTransportBoxResponse> Handle(CreateNewTransportBoxRequest request, CancellationToken cancellationToken)
    {
        // Using dispose pattern - SaveChangesAsync called automatically on dispose
        await using (_unitOfWork)
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
        // SaveChangesAsync is automatically called here when _unitOfWork is disposed
    }
}