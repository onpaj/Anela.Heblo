using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public sealed class IngestPlaudRecordingHandler : IRequestHandler<IngestPlaudRecordingRequest, IngestPlaudRecordingResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingTaskExtractor _extractor;
    private readonly ILogger<IngestPlaudRecordingHandler> _logger;

    public IngestPlaudRecordingHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingTaskExtractor extractor,
        ILogger<IngestPlaudRecordingHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<IngestPlaudRecordingResponse> Handle(
        IngestPlaudRecordingRequest request,
        CancellationToken cancellationToken)
    {
        // Check if recording already exists (idempotency)
        var exists = await _repository.ExistsByPlaudIdAsync(request.PlaudRecordingId, cancellationToken);
        if (exists)
        {
            _logger.LogDebug("Recording {RecordingId} already ingested, skipping", request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true };
        }

        // Fetch transcript and summary from Plaud
        var transcript = await _plaudClient.GetTranscriptAsync(request.PlaudRecordingId, cancellationToken);
        var summary = await _plaudClient.GetSummaryAsync(request.PlaudRecordingId, cancellationToken);

        // Extract tasks using the meeting task extractor
        var extractedTasks = await _extractor.ExtractAsync(summary, transcript, cancellationToken);

        // Create MeetingTranscript entity
        var entity = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = request.PlaudRecordingId,
            PlaudCreatedAt = request.PlaudCreatedAt,
            Subject = request.Name,
            Summary = summary,
            RawTranscript = transcript,
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = extractedTasks
                .Select(t => new ProposedTask
                {
                    Id = Guid.NewGuid(),
                    Title = t.Title,
                    Description = t.Description,
                    Assignee = t.Assignee,
                    DueDate = t.DueDate,
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                })
                .ToList()
        };

        // Persist the entity
        await _repository.AddAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // Log success
        _logger.LogInformation(
            "Ingested recording {RecordingId} ({Name}) with {TaskCount} tasks",
            request.PlaudRecordingId,
            request.Name,
            entity.Tasks.Count);

        return new IngestPlaudRecordingResponse { Success = true, TranscriptId = entity.Id };
    }
}
