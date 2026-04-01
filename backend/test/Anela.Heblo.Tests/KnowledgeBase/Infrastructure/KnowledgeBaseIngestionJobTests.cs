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
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private KnowledgeBaseIngestionJob CreateJob(KnowledgeBaseOptions options)
    {
        _statusChecker.Setup(s => s.IsJobEnabledAsync("knowledge-base-ingestion", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new KnowledgeBaseIngestionJob(
            _oneDrive.Object,
            _repository.Object,
            _mediator.Object,
            _statusChecker.Object,
            Options.Create(options),
            NullLogger<KnowledgeBaseIngestionJob>.Instance);
    }

    private static KnowledgeBaseOptions OptionsWithTwoMappings() => new()
    {
        OneDriveFolderMappings =
        [
            new() { InboxPath = "/KnowledgeBase/Inbox", ArchivedPath = "/KnowledgeBase/Archived", DocumentType = DocumentType.KnowledgeBase },
            new() { InboxPath = "/Conversation/Inbox",  ArchivedPath = "/Conversation/Archived",  DocumentType = DocumentType.Conversation  }
        ],
        OneDriveUserId = "user@test.com"
    };

    [Fact]
    public async Task ExecuteAsync_IndexesFileFromKnowledgeBaseFolder_WithCorrectDocumentType()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var kbFile = new OneDriveFile("id-kb-1", "manual.pdf", "application/pdf", "/KnowledgeBase/Inbox/manual.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/KnowledgeBase/Inbox", default)).ReturnsAsync([kbFile]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/Conversation/Inbox", default)).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("id-kb-1", default)).ReturnsAsync([1, 2, 3]);
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<IndexDocumentRequest>(r =>
                r.Filename == "manual.pdf" &&
                r.DocumentType == DocumentType.KnowledgeBase),
            default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("id-kb-1", "manual.pdf", "/KnowledgeBase/Archived", default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_IndexesFileFromConversationFolder_WithCorrectDocumentType()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var convFile = new OneDriveFile("id-conv-1", "chat.txt", "text/plain", "/Conversation/Inbox/chat.txt");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/KnowledgeBase/Inbox", default)).ReturnsAsync([]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/Conversation/Inbox", default)).ReturnsAsync([convFile]);
        _oneDrive.Setup(s => s.DownloadFileAsync("id-conv-1", default)).ReturnsAsync([4, 5, 6]);
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<IndexDocumentRequest>(r =>
                r.Filename == "chat.txt" &&
                r.DocumentType == DocumentType.Conversation),
            default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("id-conv-1", "chat.txt", "/Conversation/Archived", default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAlreadyIndexedFileByHash()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file = new OneDriveFile("id-1", "doc.pdf", "application/pdf", "/KnowledgeBase/Inbox/doc.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/KnowledgeBase/Inbox", default)).ReturnsAsync([file]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/Conversation/Inbox", default)).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("id-1", default)).ReturnsAsync([1, 2, 3]);
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new KnowledgeBaseDocument { Id = Guid.NewGuid(), SourcePath = "/KnowledgeBase/Inbox/doc.pdf" });

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<IndexDocumentRequest>(), default), Times.Never);
        _oneDrive.Verify(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
