using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Infrastructure;

public class KnowledgeBaseIngestionJobTests
{
    private readonly Mock<IOneDriveService> _oneDrive = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private KnowledgeBaseIngestionJob CreateJob(KnowledgeBaseOptions options)
    {
        _statusChecker.Setup(s => s.IsJobEnabledAsync("knowledge-base-ingestion", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new KnowledgeBaseIngestionJob(
            _oneDrive.Object,
            _mediator.Object,
            _statusChecker.Object,
            Options.Create(options),
            NullLogger<KnowledgeBaseIngestionJob>.Instance);
    }

    private static KnowledgeBaseOptions OptionsWithTwoMappings() => new()
    {
        OneDriveFolderMappings =
        [
            new() { InboxPath = "/KnowledgeBase/Inbox", ArchivedPath = "/KnowledgeBase/Archived", DocumentType = DocumentType.KnowledgeBase, DriveId = "drive-kb" },
            new() { InboxPath = "/Conversation/Inbox",  ArchivedPath = "/Conversation/Archived",  DocumentType = DocumentType.Conversation,  DriveId = "drive-conv" }
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
        _mediator.Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse { WasDuplicate = true });

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<IndexDocumentRequest>(), default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
