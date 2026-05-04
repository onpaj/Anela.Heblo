using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Infrastructure;

public class LeafletIngestionJobTests
{
    private readonly Mock<IOneDriveService> _oneDrive = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private static LeafletOptions DefaultLeafletOptions() => new()
    {
        OneDriveFolderMappings =
        [
            new OneDriveFolderMapping
            {
                DriveId = "test-drive",
                InboxPath = "/Leaflets/Inbox",
                ArchivedPath = "/Leaflets/Archived",
                DocumentType = DocumentType.Leaflet
            }
        ]
    };

    private LeafletIngestionJob CreateJob(LeafletOptions? opts = null)
    {
        opts ??= DefaultLeafletOptions();

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("leaflet-ingestion", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new LeafletIngestionJob(
            _oneDrive.Object,
            _mediator.Object,
            _statusChecker.Object,
            Options.Create(opts),
            NullLogger<LeafletIngestionJob>.Instance);
    }

    [Fact]
    public async Task Execute_calls_mediator_per_file_and_archives_on_success()
    {
        var job = CreateJob();

        var file1 = new OneDriveFile("id-1", "leaflet-a.pdf", "application/pdf", "/Leaflets/Inbox/leaflet-a.pdf");
        var file2 = new OneDriveFile("id-2", "leaflet-b.pdf", "application/pdf", "/Leaflets/Inbox/leaflet-b.pdf");

        _oneDrive
            .Setup(s => s.ListInboxFilesAsync("test-drive", "/Leaflets/Inbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync([file1, file2]);
        _oneDrive
            .Setup(s => s.DownloadFileAsync("test-drive", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        _mediator
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexLeafletResponse { WasDuplicate = false });

        await job.ExecuteAsync();

        _mediator.Verify(
            m => m.Send(It.Is<IndexLeafletRequest>(r => r.Filename == "leaflet-a.pdf"), It.IsAny<CancellationToken>()),
            Times.Once);
        _mediator.Verify(
            m => m.Send(It.Is<IndexLeafletRequest>(r => r.Filename == "leaflet-b.pdf"), It.IsAny<CancellationToken>()),
            Times.Once);
        _oneDrive.Verify(
            s => s.MoveToArchivedAsync("test-drive", "id-1", "leaflet-a.pdf", "/Leaflets/Archived", It.IsAny<CancellationToken>()),
            Times.Once);
        _oneDrive.Verify(
            s => s.MoveToArchivedAsync("test-drive", "id-2", "leaflet-b.pdf", "/Leaflets/Archived", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_archives_duplicates()
    {
        var job = CreateJob();

        var file = new OneDriveFile("id-dup", "duplicate.pdf", "application/pdf", "/Leaflets/Inbox/duplicate.pdf");

        _oneDrive
            .Setup(s => s.ListInboxFilesAsync("test-drive", "/Leaflets/Inbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync([file]);
        _oneDrive
            .Setup(s => s.DownloadFileAsync("test-drive", "id-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        _mediator
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexLeafletResponse { WasDuplicate = true });

        await job.ExecuteAsync();

        _oneDrive.Verify(
            s => s.MoveToArchivedAsync("test-drive", "id-dup", "duplicate.pdf", "/Leaflets/Archived", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_skips_unsupported_file_without_archiving()
    {
        var job = CreateJob();

        var file = new OneDriveFile("id-bad", "image.bmp", "image/bmp", "/Leaflets/Inbox/image.bmp");

        _oneDrive
            .Setup(s => s.ListInboxFilesAsync("test-drive", "/Leaflets/Inbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync([file]);
        _oneDrive
            .Setup(s => s.DownloadFileAsync("test-drive", "id-bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync([9, 9, 9]);
        _mediator
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Unsupported content type: image/bmp"));

        await job.ExecuteAsync();

        _oneDrive.Verify(
            s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_continues_after_single_file_failure()
    {
        var job = CreateJob();

        var file1 = new OneDriveFile("id-fail", "broken.pdf", "application/pdf", "/Leaflets/Inbox/broken.pdf");
        var file2 = new OneDriveFile("id-ok", "good.pdf", "application/pdf", "/Leaflets/Inbox/good.pdf");

        _oneDrive
            .Setup(s => s.ListInboxFilesAsync("test-drive", "/Leaflets/Inbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync([file1, file2]);
        _oneDrive
            .Setup(s => s.DownloadFileAsync("test-drive", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        _mediator
            .Setup(m => m.Send(It.Is<IndexLeafletRequest>(r => r.Filename == "broken.pdf"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transient failure"));
        _mediator
            .Setup(m => m.Send(It.Is<IndexLeafletRequest>(r => r.Filename == "good.pdf"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexLeafletResponse { WasDuplicate = false });

        await job.ExecuteAsync();

        _mediator.Verify(
            m => m.Send(It.Is<IndexLeafletRequest>(r => r.Filename == "good.pdf"), It.IsAny<CancellationToken>()),
            Times.Once);
        _oneDrive.Verify(
            s => s.MoveToArchivedAsync("test-drive", "id-ok", "good.pdf", "/Leaflets/Archived", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_does_nothing_when_job_is_disabled()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("leaflet-ingestion", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var job = new LeafletIngestionJob(
            _oneDrive.Object,
            _mediator.Object,
            _statusChecker.Object,
            Options.Create(DefaultLeafletOptions()),
            NullLogger<LeafletIngestionJob>.Instance);

        await job.ExecuteAsync();

        _oneDrive.Verify(
            s => s.ListInboxFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Metadata_has_unique_job_name()
    {
        var job = CreateJob();

        Assert.Equal("leaflet-ingestion", job.Metadata.JobName);
    }
}
