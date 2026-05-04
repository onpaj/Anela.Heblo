using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs;

public class LeafletIngestionJob : IRecurringJob
{
    private readonly IOneDriveService _oneDrive;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly LeafletOptions _options;
    private readonly ILogger<LeafletIngestionJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public LeafletIngestionJob(
        IOneDriveService oneDrive,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        IOptions<LeafletOptions> options,
        ILogger<LeafletIngestionJob> logger)
    {
        _oneDrive = oneDrive;
        _mediator = mediator;
        _statusChecker = statusChecker;
        _options = options.Value;
        _logger = logger;

        Metadata = new RecurringJobMetadata
        {
            JobName = "leaflet-ingestion",
            DisplayName = "Leaflet Ingestion",
            Description = "Polls OneDrive Leaflets/Inbox folder and ingests new leaflet documents into the leaflet vector store",
            CronExpression = _options.IngestionCronExpression,
            DefaultIsEnabled = true
        };
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

        foreach (var folder in _options.OneDriveFolderMappings.Where(m => m.DocumentType == DocumentType.Leaflet))
        {
            var files = await _oneDrive.ListInboxFilesAsync(folder.DriveId, folder.InboxPath, cancellationToken);
            _logger.LogInformation("Found {Count} files in {InboxPath}", files.Count, folder.InboxPath);

            foreach (var file in files)
            {
                try
                {
                    var content = await _oneDrive.DownloadFileAsync(folder.DriveId, file.Id, cancellationToken);

                    var result = await _mediator.Send(new IndexLeafletRequest
                    {
                        Filename = file.Name,
                        SourcePath = file.Path,
                        ContentType = file.ContentType,
                        Content = content
                    }, cancellationToken);

                    await _oneDrive.MoveToArchivedAsync(folder.DriveId, file.Id, file.Name, folder.ArchivedPath, cancellationToken);

                    if (result.WasDuplicate)
                    {
                        _logger.LogInformation("Duplicate {Filename} archived to prevent reprocessing", file.Name);
                        skipped++;
                    }
                    else
                    {
                        _logger.LogInformation("Indexed and archived {Filename}", file.Name);
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
