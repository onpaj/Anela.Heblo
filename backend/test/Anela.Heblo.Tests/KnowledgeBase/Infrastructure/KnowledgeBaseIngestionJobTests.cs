using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Infrastructure;

public class KnowledgeBaseIngestionJobTests
{
    private readonly Mock<IOneDriveService> _oneDrive = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();
    private readonly Mock<IKnowledgeBaseRepository> _knowledgeBaseRepository = new();
    private readonly Mock<ILogger<KnowledgeBaseIngestionJob>> _logger = new();

    private KnowledgeBaseIngestionJob CreateJob(KnowledgeBaseOptions options)
    {
        _statusChecker.Setup(s => s.IsJobEnabledAsync("knowledge-base-ingestion", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new KnowledgeBaseIngestionJob(
            _oneDrive.Object,
            _mediator.Object,
            _statusChecker.Object,
            _knowledgeBaseRepository.Object,
            Options.Create(options),
            _logger.Object);
    }

    private static KnowledgeBaseOptions OptionsWithTwoMappings() => new()
    {
        OneDriveFolderMappings =
        [
            new() { InboxPath = "/KnowledgeBase/Inbox", ArchivedPath = "/KnowledgeBase/Archived", DocumentType = DocumentType.KnowledgeBase, DriveId = "drive-kb" },
            new() { InboxPath = "/Conversation/Inbox", ArchivedPath = "/Conversation/Archived", DocumentType = DocumentType.Conversation, DriveId = "drive-conv" }
        ]
    };

    [Fact]
    public async Task ExecuteAsync_IndexesFileFromKnowledgeBaseFolder_WithCorrectDocumentType()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var kbFile = new OneDriveFile("id-kb-1", "manual.pdf", "application/pdf", "/KnowledgeBase/Inbox/manual.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([kbFile]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("drive-kb", "id-kb-1", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _oneDrive.Setup(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://mock.sharepoint.com/archived/manual.pdf");
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { WasDuplicate = false });

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<IndexDocumentRequest>(r =>
                r.Filename == "manual.pdf" &&
                r.DocumentType == DocumentType.KnowledgeBase),
            default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("drive-kb", "id-kb-1", "manual.pdf", "/KnowledgeBase/Archived", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_IndexesFileFromConversationFolder_WithCorrectDocumentType()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var convFile = new OneDriveFile("id-conv-1", "chat.txt", "text/plain", "/Conversation/Inbox/chat.txt");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([convFile]);
        _oneDrive.Setup(s => s.DownloadFileAsync("drive-conv", "id-conv-1", It.IsAny<CancellationToken>())).ReturnsAsync([4, 5, 6]);
        _oneDrive.Setup(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://mock.sharepoint.com/archived/chat.txt");
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { WasDuplicate = false });

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<IndexDocumentRequest>(r =>
                r.Filename == "chat.txt" &&
                r.DocumentType == DocumentType.Conversation),
            default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("drive-conv", "id-conv-1", "chat.txt", "/Conversation/Archived", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAlreadyIndexedFileByHash()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file = new OneDriveFile("id-1", "doc.pdf", "application/pdf", "/KnowledgeBase/Inbox/doc.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([file]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("drive-kb", "id-1", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _oneDrive.Setup(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://mock.sharepoint.com/archived/doc.pdf");
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { WasDuplicate = true });

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<IndexDocumentRequest>(), default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("drive-kb", "id-1", "doc.pdf", "/KnowledgeBase/Archived", default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_passes_DriveId_and_GraphItemId_in_request()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file = new OneDriveFile("graph-item-id-kb", "manual.pdf", "application/pdf", "/KnowledgeBase/Inbox/manual.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([file]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("drive-kb", "graph-item-id-kb", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _oneDrive.Setup(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://mock.sharepoint.com/archived/manual.pdf");
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { WasDuplicate = false });

        await job.ExecuteAsync();

        _mediator.Verify(
            m => m.Send(
                It.Is<IndexDocumentRequest>(r =>
                    r.DriveId == "drive-kb" &&
                    r.GraphItemId == "graph-item-id-kb"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_calls_UpdateDocumentSourcePathAsync_with_archive_url_after_move()
    {
        var documentId = Guid.NewGuid();
        const string archiveUrl = "https://mock.sharepoint.com/archived/manual.pdf";
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file = new OneDriveFile("graph-item-id-kb", "manual.pdf", "application/pdf", "/KnowledgeBase/Inbox/manual.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([file]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("drive-kb", "graph-item-id-kb", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _oneDrive
            .Setup(s => s.MoveToArchivedAsync("drive-kb", "graph-item-id-kb", "manual.pdf", "/KnowledgeBase/Archived", It.IsAny<CancellationToken>()))
            .ReturnsAsync(archiveUrl);
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { DocumentId = documentId, WasDuplicate = false });

        await job.ExecuteAsync();

        _knowledgeBaseRepository.Verify(
            r => r.UpdateDocumentSourcePathAsync(documentId, archiveUrl, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_calls_UpdateDocumentSourcePathAsync_for_duplicate_document()
    {
        // UpdateDocumentSourcePathAsync must run even for duplicates so the DB SourcePath
        // points to the new archive location, not the stale inbox path.
        var documentId = Guid.NewGuid();
        const string archiveUrl = "https://mock.sharepoint.com/archived/doc.pdf";
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file = new OneDriveFile("id-dup-kb", "doc.pdf", "application/pdf", "/KnowledgeBase/Inbox/doc.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([file]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("drive-kb", "id-dup-kb", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _oneDrive
            .Setup(s => s.MoveToArchivedAsync("drive-kb", "id-dup-kb", "doc.pdf", "/KnowledgeBase/Archived", It.IsAny<CancellationToken>()))
            .ReturnsAsync(archiveUrl);
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { DocumentId = documentId, WasDuplicate = true });

        await job.ExecuteAsync();

        _knowledgeBaseRepository.Verify(
            r => r.UpdateDocumentSourcePathAsync(documentId, archiveUrl, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_logs_warning_and_continues_when_UpdateDocumentSourcePathAsync_throws()
    {
        // If MoveToArchivedAsync succeeds but UpdateDocumentSourcePathAsync throws, the job should:
        // - log a targeted warning (not the generic "Failed to index" error)
        // - continue processing the next file without incrementing failed
        var documentId = Guid.NewGuid();
        const string archiveUrl = "https://mock.sharepoint.com/archived/manual.pdf";
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file1 = new OneDriveFile("id-fail-path", "manual.pdf", "application/pdf", "/KnowledgeBase/Inbox/manual.pdf");
        var file2 = new OneDriveFile("id-ok", "other.pdf", "application/pdf", "/KnowledgeBase/Inbox/other.pdf");

        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-kb", "/KnowledgeBase/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([file1, file2]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("drive-conv", "/Conversation/Inbox", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _oneDrive
            .Setup(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(archiveUrl);
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { DocumentId = documentId, WasDuplicate = false });

        _knowledgeBaseRepository
            .Setup(r => r.UpdateDocumentSourcePathAsync(documentId, archiveUrl, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        await job.ExecuteAsync();

        // Warning logged with "Manual correction required" marker — distinct from generic "Failed to index"
        // Verify it includes the archiveUrl in the log message
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Manual correction required") && v.ToString()!.Contains(archiveUrl)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // Both files were attempted — job continued past the SourcePath failure
        _mediator.Verify(
            m => m.Send(It.Is<IndexDocumentRequest>(r => r.Filename == "other.pdf"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
