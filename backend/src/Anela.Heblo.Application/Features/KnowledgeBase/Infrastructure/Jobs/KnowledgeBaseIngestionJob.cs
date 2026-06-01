using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;

public class KnowledgeBaseIngestionJob : IRecurringJob
{
    private readonly IOneDriveService _oneDrive;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<KnowledgeBaseIngestionJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "knowledge-base-ingestion",
        DisplayName = "Knowledge Base Ingestion",
        Description = "Polls OneDrive inbox folders and ingests new documents into the knowledge base vector store",
        CronExpression = "*/15 * * * *",
        DefaultIsEnabled = true
    };

    public KnowledgeBaseIngestionJob(
        IOneDriveService oneDrive,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        IKnowledgeBaseRepository knowledgeBaseRepository,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<KnowledgeBaseIngestionJob> logger)
    {
        _oneDrive = oneDrive;
        _mediator = mediator;
        _statusChecker = statusChecker;
        _knowledgeBaseRepository = knowledgeBaseRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        int indexed = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var mapping in _options.OneDriveFolderMappings)
        {
            _logger.LogInformation("Polling {InboxPath} ({DocumentType})", mapping.InboxPath, mapping.DocumentType);

            var files = await _oneDrive.ListInboxFilesAsync(mapping.DriveId, mapping.InboxPath, cancellationToken);
            _logger.LogInformation("Found {Count} files in {InboxPath}", files.Count, mapping.InboxPath);

            foreach (var file in files)
            {
                try
                {
                    var content = await _oneDrive.DownloadFileAsync(mapping.DriveId, file.Id, cancellationToken);

                    var result = await _mediator.Send(new IndexDocumentRequest
                    {
                        Filename = file.Name,
                        SourcePath = file.Path,
                        ContentType = file.ContentType,
                        Content = content,
                        DocumentType = mapping.DocumentType,
                        DriveId = mapping.DriveId,
                        GraphItemId = file.Id
                    }, cancellationToken);

                    var archiveUrl = await _oneDrive.MoveToArchivedAsync(mapping.DriveId, file.Id, file.Name, mapping.ArchivedPath, cancellationToken);

                    // UpdateDocumentSourcePathAsync is called for both new and duplicate documents — intentional.
                    // For duplicates the matched document's SourcePath is updated to the new archive location
                    // so the DB link stays current even when the same content arrives again.
                    try
                    {
                        await _knowledgeBaseRepository.UpdateDocumentSourcePathAsync(result.DocumentId, archiveUrl, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "File {Name} was moved to archive {ArchiveUrl} but SourcePath update failed for document {DocumentId}. Manual correction required.",
                            file.Name, archiveUrl, result.DocumentId);
                    }

                    if (result.WasDuplicate)
                    {
                        _logger.LogInformation("Duplicate {Filename} archived to prevent reprocessing", file.Name);
                        skipped++;
                    }
                    else
                    {
                        _logger.LogInformation("Indexed and archived {Filename} as {DocumentType}", file.Name, mapping.DocumentType);
                        indexed++;
                    }
                }
                catch (NotSupportedException ex)
                {
                    _logger.LogWarning("Skipping unsupported file {Filename}: {Message}", file.Name, ex.Message);
                    skipped++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to index {Filename}", file.Name);
                    failed++;
                }
            }
        }

        _logger.LogInformation("{JobName} complete. Indexed: {Indexed}, Skipped: {Skipped}, Failed: {Failed}",
            Metadata.JobName, indexed, skipped, failed);
    }
}
