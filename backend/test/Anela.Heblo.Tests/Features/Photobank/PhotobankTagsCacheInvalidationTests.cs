using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds;
using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankTagsCacheInvalidationTests
{
    private readonly Mock<IPhotobankPhotoRepository> _photoRepo = new();
    private readonly Mock<IPhotobankTagRepository> _tagRepo = new();
    private readonly Mock<IPhotobankPhotoTagRepository> _photoTagRepo = new();
    private readonly Mock<IPhotobankTagRuleRepository> _tagRuleRepo = new();
    private readonly Mock<IPhotobankAutoTagRepository> _autoTagRepo = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();

    [Fact]
    public async Task CreateTag_InvalidatesCache_WhenNewTagCreated()
    {
        _tagRepo.Setup(r => r.GetTagByNameAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync((Tag?)null);
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 1, Name = "summer" });

        var handler = new CreateTagHandler(_tagRepo.Object, _cache.Object);
        await handler.Handle(new CreateTagRequest { Name = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task CreateTag_DoesNotInvalidate_WhenTagAlreadyExisted()
    {
        _tagRepo.Setup(r => r.GetTagByNameAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 1, Name = "summer" });

        var handler = new CreateTagHandler(_tagRepo.Object, _cache.Object);
        await handler.Handle(new CreateTagRequest { Name = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task DeleteTag_InvalidatesCache_AfterSave()
    {
        var tag = new Tag { Id = 1, Name = "summer" };
        _tagRepo.Setup(r => r.GetTagByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(tag);

        var handler = new DeleteTagHandler(_tagRepo.Object, _cache.Object);
        await handler.Handle(new DeleteTagRequest { Id = 1 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task DeleteTag_DoesNotInvalidate_WhenTagNotFound()
    {
        _tagRepo.Setup(r => r.GetTagByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Tag?)null);

        var handler = new DeleteTagHandler(_tagRepo.Object, _cache.Object);
        await handler.Handle(new DeleteTagRequest { Id = 99 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task AddPhotoTag_InvalidatesCache_WhenNewTagAttached()
    {
        _photoRepo.Setup(r => r.GetPhotoByIdAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Photo { Id = 1, SharePointFileId = "sp", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow });
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "summer" });
        _photoTagRepo.Setup(r => r.PhotoTagExistsAsync(1, 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var handler = new AddPhotoTagHandler(_photoRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(new AddPhotoTagRequest { PhotoId = 1, TagName = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task AddPhotoTag_DoesNotInvalidate_WhenTagAlreadyAttached()
    {
        _photoRepo.Setup(r => r.GetPhotoByIdAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Photo { Id = 1, SharePointFileId = "sp", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow });
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "summer" });
        _photoTagRepo.Setup(r => r.PhotoTagExistsAsync(1, 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var handler = new AddPhotoTagHandler(_photoRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(new AddPhotoTagRequest { PhotoId = 1, TagName = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task RemovePhotoTag_InvalidatesCache_AfterSave()
    {
        _photoRepo.Setup(r => r.GetPhotoByIdAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Photo { Id = 1, SharePointFileId = "sp", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow });

        var handler = new RemovePhotoTagHandler(_photoRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(new RemovePhotoTagRequest { PhotoId = 1, TagId = 5 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task RemovePhotoTag_DoesNotInvalidate_WhenPhotoNotFound()
    {
        _photoRepo.Setup(r => r.GetPhotoByIdAsync(999, It.IsAny<CancellationToken>()))
             .ReturnsAsync((Photo?)null);

        var handler = new RemovePhotoTagHandler(_photoRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(new RemovePhotoTagRequest { PhotoId = 999, TagId = 5 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task BulkAddPhotoTag_InvalidatesCache_WhenPhotosTagged()
    {
        _photoRepo.Setup(r => r.CountFilteredPhotosAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(3);
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "summer" });
        _photoRepo.Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int> { 1, 2 });

        var handler = new BulkAddPhotoTagHandler(_photoRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(new BulkAddPhotoTagRequest { TagName = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task BulkAddPhotoTag_DoesNotInvalidate_WhenNoPhotosNeedTagging()
    {
        _photoRepo.Setup(r => r.CountFilteredPhotosAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(3);
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "summer" });
        _photoRepo.Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int>());

        var handler = new BulkAddPhotoTagHandler(_photoRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(new BulkAddPhotoTagRequest { TagName = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task BulkAddPhotoTagByIds_InvalidatesCache_WhenPhotosTagged()
    {
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "summer" });
        _photoRepo.Setup(r => r.GetExistingPhotoIdsMissingTagAsync(It.IsAny<IReadOnlyList<int>>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int> { 1, 2 });
        _photoRepo.Setup(r => r.CountExistingPhotosAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(2);

        var handler = new BulkAddPhotoTagByIdsHandler(_photoRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(
            new BulkAddPhotoTagByIdsRequest { TagName = "summer", PhotoIds = new List<int> { 1, 2 } },
            CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task BulkAddPhotoTagByIds_DoesNotInvalidate_WhenNoPhotosToTag()
    {
        _tagRepo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "summer" });
        _photoRepo.Setup(r => r.GetExistingPhotoIdsMissingTagAsync(It.IsAny<IReadOnlyList<int>>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int>());
        _photoRepo.Setup(r => r.CountExistingPhotosAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(2);

        var handler = new BulkAddPhotoTagByIdsHandler(_photoRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _cache.Object);
        await handler.Handle(
            new BulkAddPhotoTagByIdsRequest { TagName = "summer", PhotoIds = new List<int> { 1, 2 } },
            CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task ReapplyRules_InvalidatesCache_AfterSave()
    {
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>());
        _photoTagRepo.Setup(r => r.RemoveRuleTagsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _photoTagRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var handler = new ReapplyRulesHandler(_tagRuleRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _autoTagRepo.Object, _cache.Object);
        await handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task RetagPhotos_InvalidatesCache_WhenPhotosFound()
    {
        _photoRepo.Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Photo>
             {
                 new() { Id = 1, SharePointFileId = "sp-1", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow },
             });
        _autoTagRepo.Setup(r => r.ResetAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var bgWorker = new Mock<IBackgroundWorker>();
        bgWorker.Setup(w => w.Enqueue<PhotobankAutoTagJob>(It.IsAny<Expression<Func<PhotobankAutoTagJob, Task>>>()))
                .Returns("job-1");

        var handler = new RetagPhotosHandler(_photoRepo.Object, _photoTagRepo.Object, _autoTagRepo.Object, bgWorker.Object, _cache.Object);
        await handler.Handle(
            new RetagPhotosRequest { PhotoIds = new[] { 1 }, ClearExistingAiTags = false },
            CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task RetagPhotos_DoesNotInvalidate_WhenNoPhotosFound()
    {
        _photoRepo.Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Photo>());
        var bgWorker = new Mock<IBackgroundWorker>();

        var handler = new RetagPhotosHandler(_photoRepo.Object, _photoTagRepo.Object, _autoTagRepo.Object, bgWorker.Object, _cache.Object);
        await handler.Handle(
            new RetagPhotosRequest { PhotoIds = new[] { 1 } },
            CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }
}
