using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;

public class OpenOrResumeBoxByCodeHandler : IRequestHandler<OpenOrResumeBoxByCodeRequest, OpenOrResumeBoxByCodeResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OpenOrResumeBoxByCodeHandler> _logger;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public OpenOrResumeBoxByCodeHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<OpenOrResumeBoxByCodeHandler> logger,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<OpenOrResumeBoxByCodeResponse> Handle(OpenOrResumeBoxByCodeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.BoxCode))
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.RequiredFieldMissing,
                    new Dictionary<string, string> { { "field", "BoxCode" } });
            }

            var code = request.BoxCode.Trim().ToUpper();
            var user = _currentUserService.GetCurrentUser();
            var userName = user.IsAuthenticated ? user.Name ?? "Unknown User" : "Anonymous";
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var existing = await _repository.GetByCodeAsync(code);

            // Resume an in-progress box.
            if (existing != null && existing.State == TransportBoxState.Opened)
            {
                return new OpenOrResumeBoxByCodeResponse
                {
                    TransportBox = _mapper.Map<TransportBoxDto>(existing),
                    Resumed = true
                };
            }

            // A box with this code is busy in a non-resumable state.
            if (existing != null && existing.State != TransportBoxState.Closed && existing.State != TransportBoxState.Stocked)
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
            }

            // No box, or only a Closed/Stocked box with this code — create and open a fresh one.
            // GetByCodeAsync returns any active box first, so reaching here means none exists.
            var box = new TransportBox
            {
                CreatorId = Guid.TryParse(user.Id, out var userId) ? userId : null,
                CreationTime = now,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                ExtraProperties = "{}"
            };
            box.Open(code, now, userName);

            await _repository.AddAsync(box, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Opened new transport box {Code} (id {BoxId}) by {User}", code, box.Id, userName);

            return new OpenOrResumeBoxByCodeResponse
            {
                TransportBox = _mapper.Map<TransportBoxDto>(box),
                Resumed = false
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error opening box by code {Code}: {Error}", request.BoxCode, ex.Message);
            return new OpenOrResumeBoxByCodeResponse(ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "details", ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening box by code {Code}", request.BoxCode);
            return new OpenOrResumeBoxByCodeResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "details", ex.Message } });
        }
    }
}
