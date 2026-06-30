using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// Read-only MCP tools for Meeting Notes (MeetingTasks feature).
/// Thin wrappers around existing MediatR read handlers. Per-meeting visibility is enforced
/// inside the handlers via ICurrentUserService / IMeetingAccessGuard; these tools add an
/// explicit anela.meetings.read gate mirroring the controller's [FeatureAuthorize].
/// </summary>
[McpServerToolType]
public class MeetingTasksMcpTools
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<MeetingTasksMcpTools> _logger;

    public MeetingTasksMcpTools(
        IMediator mediator,
        ICurrentUserService currentUserService,
        ILogger<MeetingTasksMcpTools> logger)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [McpServerTool]
    [Description("List meeting notes (summary level: subject, summary, status, task counts; no raw transcript or task detail). Supports search, status filter, and pagination. Returns only meetings the caller is allowed to see.")]
    public async Task<string> ListMeetings(
        [Description("Search term matched against subject and summary")]
        string? searchText = null,
        [Description("Filter by status: PendingReview, Approved, or PartiallyApproved")]
        string? statusFilter = null,
        [Description("Also search inside the raw transcript text (default: false)")]
        bool searchInTranscript = false,
        [Description("Page number for pagination (default: 1)")]
        int pageNumber = 1,
        [Description("Page size for pagination (default: 20)")]
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess();

        try
        {
            var request = new GetTranscriptListRequest
            {
                SearchText = searchText,
                StatusFilter = statusFilter,
                SearchInTranscript = searchInTranscript,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            var response = await _mediator.Send(request, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                response.Items,
                response.TotalCount,
                response.PageNumber,
                response.PageSize
            });
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP ListMeetings failed");
            throw new McpException($"Failed to list meetings: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Get the summary and metadata of a single meeting (no raw transcript, no task detail). Use GetMeetingTranscript or GetMeetingTasks for those.")]
    public async Task<string> GetMeetingSummary(
        [Description("Meeting id (GUID)")] Guid id,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess();

        try
        {
            var transcript = await GetTranscriptOrThrow(id, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                transcript.Id,
                transcript.Subject,
                transcript.Summary,
                transcript.Status,
                transcript.PlaudCreatedAt,
                transcript.ReceivedAt,
                transcript.ReviewedAt,
                transcript.ReviewedByUser,
                transcript.TaskCount,
                transcript.ApprovedTaskCount,
                transcript.RejectedTaskCount,
                transcript.AccessLevel
            });
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP GetMeetingSummary failed for meeting {MeetingId}", id);
            throw new McpException($"Failed to get meeting summary: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Get the full raw transcript text of a single meeting. May be large.")]
    public async Task<string> GetMeetingTranscript(
        [Description("Meeting id (GUID)")] Guid id,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess();

        try
        {
            var transcript = await GetTranscriptOrThrow(id, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                transcript.Id,
                transcript.Subject,
                transcript.RawTranscript
            });
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP GetMeetingTranscript failed for meeting {MeetingId}", id);
            throw new McpException($"Failed to get meeting transcript: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Get the proposed task list extracted from a single meeting.")]
    public async Task<string> GetMeetingTasks(
        [Description("Meeting id (GUID)")] Guid id,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess();

        try
        {
            var transcript = await GetTranscriptOrThrow(id, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                transcript.Id,
                transcript.Subject,
                transcript.Tasks
            });
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP GetMeetingTasks failed for meeting {MeetingId}", id);
            throw new McpException($"Failed to get meeting tasks: {ex.Message}");
        }
    }

    private void EnsureReadAccess()
    {
        var role = AccessRoles.For(Feature.Anela_Meetings, AccessLevel.Read);
        if (!_currentUserService.IsInRole(role))
        {
            throw new McpException(
                $"[FORBIDDEN] You do not have permission to access Meeting Notes (requires {role}).");
        }
    }

    private async Task<Application.Features.MeetingTasks.Contracts.MeetingTranscriptDto> GetTranscriptOrThrow(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetTranscriptDetailRequest { Id = id }, cancellationToken);

        if (!response.Success)
        {
            var code = response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR";
            // FullError() dereferences Params, which is null when the handler reports an error
            // without parameters (e.g. ResourceNotFound) — only use it when params are present.
            var detail = response.Params is { Count: > 0 } ? response.FullError() : code;
            throw new McpException($"[{code}] {detail}");
        }

        return response.Transcript;
    }
}
