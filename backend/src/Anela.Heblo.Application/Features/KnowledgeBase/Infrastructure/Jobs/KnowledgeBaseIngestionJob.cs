using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;

public class KnowledgeBaseIngestionJob : IRecurringJob
{
    private readonly IOneDriveService _oneDrive;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
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
        IKnowledgeBaseRepository repository,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<KnowledgeBaseIngestionJob> logger)
    {
        _oneDrive = oneDrive;
        _repository = repository;
        _mediator = mediator;
        _statusChecker = statusChecker;
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

                    var contentHash = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

                    var existingDocument = await _repository.GetDocumentByHashAsync(contentHash, cancellationToken);
                    if (existingDocument is not null)
                    {
                        if (existingDocument.SourcePath != file.Path)
                        {
                            _logger.LogInformation("File {Filename} moved, updating path from {OldPath} to {NewPath}",
                                file.Name, existingDocument.SourcePath, file.Path);
                            await _repository.UpdateDocumentSourcePathAsync(existingDocument.Id, file.Path, cancellationToken);
                        }
                        else
                        {
                            _logger.LogDebug("Skipping already-indexed file {Filename} (hash match)", file.Name);
                        }

                        skipped++;
                        continue;
                    }

                    var existingByPath = await _repository.GetDocumentBySourcePathAsync(file.Path, cancellationToken);
                    if (existingByPath is not null)
                    {
                        _logger.LogInformation(
                            "File {Filename} at {Path} has new content (hash changed). Deleting old document {Id} before re-indexing.",
                            file.Name, file.Path, existingByPath.Id);
                        await _repository.DeleteDocumentAsync(existingByPath.Id, cancellationToken);
                    }

                    await _mediator.Send(new IndexDocumentRequest
                    {
                        Filename = file.Name,
                        SourcePath = file.Path,
                        ContentType = file.ContentType,
                        Content = content,
                        ContentHash = contentHash,
                        DocumentType = mapping.DocumentType
                    }, cancellationToken);

                    await _oneDrive.MoveToArchivedAsync(mapping.DriveId, file.Id, file.Name, mapping.ArchivedPath, cancellationToken);

                    _logger.LogInformation("Indexed and archived {Filename} as {DocumentType}", file.Name, mapping.DocumentType);
                    indexed++;
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
