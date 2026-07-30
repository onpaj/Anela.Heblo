using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class ReapplyRulesHandlerTests
{
    private readonly Mock<IPhotobankTagRuleRepository> _tagRuleRepo = new();
    private readonly Mock<IPhotobankTagRepository> _tagRepo = new();
    private readonly Mock<IPhotobankPhotoTagRepository> _photoTagRepo = new();
    private readonly Mock<IPhotobankAutoTagRepository> _autoTagRepo = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();
    private readonly ReapplyRulesHandler _handler;

    public ReapplyRulesHandlerTests()
    {
        _handler = new ReapplyRulesHandler(_tagRuleRepo.Object, _tagRepo.Object, _photoTagRepo.Object, _autoTagRepo.Object, _cache.Object);

        // Sensible defaults so unconfigured calls don't NRE.
        _photoTagRepo.Setup(r => r.RemoveRuleTagsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _photoTagRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _photoTagRepo.Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new HashSet<(int PhotoId, int TagId)>());
        _autoTagRepo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<PhotoAutoTagCandidate>());
        _photoTagRepo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _tagRepo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int>());
    }

    private static PhotoAutoTagCandidate CandidateAt(int id, string folder, string file) =>
        new(id, folder, file);

    [Fact]
    public async Task RuleNotFound_ReturnsError_AndDoesNotRemoveOrSave()
    {
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>());

        var result = await _handler.Handle(new ReapplyRulesRequest { RuleId = 99 }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PhotobankRuleNotFound);
        _photoTagRepo.Verify(r => r.RemoveRuleTagsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _photoTagRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoActiveRuleTagNames_CommitsRemovalThenReturnsZero_AndInvalidates()
    {
        // All rules inactive → ruleTagNames empty. Removal must still be committed (behavior preservation).
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "P", TagName = "products", IsActive = false, SortOrder = 0 },
        });

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PhotosUpdated.Should().Be(0);
        _photoTagRepo.Verify(r => r.RemoveRuleTagsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once); // removal committed
        _tagRepo.Verify(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _photoTagRepo.Verify(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task HappyPath_AddsRuleTags_CountsPhotos_InvalidatesOnce()
    {
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
        });
        _tagRepo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _autoTagRepo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/A", "a.jpg"),
            CandidateAt(2, "Products/B", "b.jpg"),
            CandidateAt(3, "Events/C", "c.jpg"), // no match
        });

        List<PhotoTag>? added = null;
        _photoTagRepo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.PhotosUpdated.Should().Be(2);
        added.Should().NotBeNull();
        added!.Should().HaveCount(2);
        added.Should().OnlyContain(t => t.Source == PhotoTagSource.Rule && t.TagId == 10);
        added.Select(t => t.PhotoId).Should().BeEquivalentTo(new[] { 1, 2 });
        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task ManualAiPrecedence_OccupiedPairNotAdded()
    {
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
        });
        _tagRepo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _autoTagRepo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/A", "a.jpg"),
        });
        _photoTagRepo.Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new HashSet<(int PhotoId, int TagId)> { (1, 10) }); // Manual/AI already owns it

        List<PhotoTag>? added = null;
        _photoTagRepo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.PhotosUpdated.Should().Be(0);
        added!.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateMatch_CountedOnce()
    {
        // Two active rules produce the SAME tag name and both match the photo.
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
            new() { Id = 2, PathPattern = "A", TagName = "products", IsActive = true, SortOrder = 1 },
        });
        _tagRepo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _autoTagRepo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/A", "a.jpg"), // matches both rules → still one (1,10) pair
        });

        List<PhotoTag>? added = null;
        _photoTagRepo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.PhotosUpdated.Should().Be(1);
        added!.Should().ContainSingle();
    }

    [Fact]
    public async Task SingleRule_ScopesEveryStepToTagName()
    {
        _tagRuleRepo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "Products", IsActive = true, SortOrder = 0 },
            new() { Id = 2, PathPattern = "Events", TagName = "events", IsActive = true, SortOrder = 1 },
        });
        _autoTagRepo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/Events", "a.jpg"), // matches both rules' patterns
        });

        IReadOnlyCollection<string>? requestedNames = null;
        _tagRepo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .Callback<IReadOnlyCollection<string>, CancellationToken>((names, _) => requestedNames = names)
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });

        List<PhotoTag>? added = null;
        _photoTagRepo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest { RuleId = 1 }, CancellationToken.None);

        // scope = "products" (lowercased) threaded through removal, occupied snapshot, tag set
        _photoTagRepo.Verify(r => r.RemoveRuleTagsAsync("products", It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepo.Verify(r => r.GetOccupiedTagPairsAsync("products", It.IsAny<CancellationToken>()), Times.Once);
        requestedNames.Should().BeEquivalentTo(new[] { "products" }); // "events" excluded by scope
        added!.Should().OnlyContain(t => t.TagId == 10);
        result.PhotosUpdated.Should().Be(1);
    }
}
