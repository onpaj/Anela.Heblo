using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;

public class UpdateMeetingAccessHandler : IRequestHandler<UpdateMeetingAccessRequest, UpdateMeetingAccessResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<UpdateMeetingAccessHandler> _logger;

    public UpdateMeetingAccessHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ICurrentUserService currentUserService,
        IMeetingUserDirectory userDirectory,
        ILogger<UpdateMeetingAccessHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _currentUserService = currentUserService;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    public async Task<UpdateMeetingAccessResponse> Handle(UpdateMeetingAccessRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating meeting access — TranscriptId: {TranscriptId}, AccessLevel: {AccessLevel}",
            request.TranscriptId, request.AccessLevel);

        if (!_accessGuard.IsManager())
        {
            _logger.LogWarning("Non-manager attempted to update meeting access for {TranscriptId}", request.TranscriptId);
            return new UpdateMeetingAccessResponse(ErrorCodes.Forbidden);
        }

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateMeetingAccessResponse(ErrorCodes.ResourceNotFound);
        }

        if (!Enum.TryParse<MeetingAccessLevel>(request.AccessLevel, ignoreCase: true, out var level))
        {
            return new UpdateMeetingAccessResponse(ErrorCodes.ValidationError,
                new Dictionary<string, string> { ["accessLevel"] = $"Unknown access level: {request.AccessLevel}" });
        }

        var grants = new List<MeetingAccessGrant>();

        if (level == MeetingAccessLevel.Restricted)
        {
            if (request.RestrictedUserEmails.Count == 0)
            {
                return new UpdateMeetingAccessResponse(ErrorCodes.ValidationError,
                    new Dictionary<string, string> { ["restrictedUserEmails"] = "At least one email is required for Restricted access" });
            }

            var allUsers = _userDirectory.GetAll();

            foreach (var rawEmail in request.RestrictedUserEmails)
            {
                var normalizedEmail = rawEmail.Trim().ToLowerInvariant();
                var knownUser = allUsers.FirstOrDefault(u =>
                    u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));

                if (knownUser is null)
                {
                    return new UpdateMeetingAccessResponse(ErrorCodes.ValidationError,
                        new Dictionary<string, string> { ["email"] = normalizedEmail });
                }

                grants.Add(new MeetingAccessGrant
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcript.Id,
                    UserEmail = normalizedEmail,
                    UserDisplayName = knownUser.DisplayName,
                    GrantedAt = DateTime.UtcNow,
                    GrantedByUserEmail = _currentUserService.GetCurrentUser().Email ?? string.Empty
                });
            }
        }

        await _repository.SetAccessAsync(transcript, level, grants, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Meeting access updated — TranscriptId: {TranscriptId}, AccessLevel: {AccessLevel}, Grants: {GrantCount}",
            request.TranscriptId, level, grants.Count);

        return new UpdateMeetingAccessResponse
        {
            AccessLevel = level.ToString(),
            Grants = grants.Select(g => new MeetingAccessGrantDto
            {
                UserEmail = g.UserEmail,
                UserDisplayName = g.UserDisplayName
            }).ToList()
        };
    }
}
