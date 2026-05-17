using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;

public class ExplainSummaryHandler : IRequestHandler<ExplainSummaryRequest, ExplainSummaryResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingSummaryExplainer _explainer;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<ExplainSummaryHandler> _logger;

    public ExplainSummaryHandler(
        IMeetingTranscriptRepository repository,
        IMeetingSummaryExplainer explainer,
        IMeetingAccessGuard accessGuard,
        ILogger<ExplainSummaryHandler> logger)
    {
        _repository = repository;
        _explainer = explainer;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<ExplainSummaryResponse> Handle(
        ExplainSummaryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Explaining summary fragment — TranscriptId: {TranscriptId}",
            request.TranscriptId);

        if (string.IsNullOrWhiteSpace(request.SelectedText))
        {
            _logger.LogWarning("ExplainSummary called with empty SelectedText");
            return new ExplainSummaryResponse(ErrorCodes.RequiredFieldMissing);
        }

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new ExplainSummaryResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new ExplainSummaryResponse(ErrorCodes.ResourceNotFound);
        }

        var result = await _explainer.ExplainAsync(
            transcript.RawTranscript,
            request.SelectedText,
            cancellationToken);

        return new ExplainSummaryResponse
        {
            RelevantTranscript = result.RelevantTranscript,
            Explanation = result.Explanation,
        };
    }
}
