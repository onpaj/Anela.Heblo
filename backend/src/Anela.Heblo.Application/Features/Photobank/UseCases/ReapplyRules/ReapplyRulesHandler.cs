using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules
{
    public class ReapplyRulesHandler : IRequestHandler<ReapplyRulesRequest, ReapplyRulesResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public ReapplyRulesHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<ReapplyRulesResponse> Handle(ReapplyRulesRequest request, CancellationToken cancellationToken)
        {
            var allRules = await _repository.GetRulesAsync(cancellationToken);

            string? scopeToTagName = null;
            if (request.RuleId.HasValue)
            {
                var rule = allRules.FirstOrDefault(r => r.Id == request.RuleId.Value);
                if (rule == null)
                    return new ReapplyRulesResponse(ErrorCodes.PhotobankRuleNotFound);

                scopeToTagName = rule.TagName.ToLowerInvariant();
            }

            var activeRules = allRules.Where(r => r.IsActive).ToList();

            // Remove existing Rule-sourced tags (scoped) and commit first. Committing the
            // deletions detaches them before we re-add the same (PhotoId, TagId) pairs,
            // avoiding the EF change-tracker collision on no-op re-applies (shared composite PK).
            // This is also unconditional: the previous implementation always committed the
            // removal (the handler saved even when the repository returned 0), so removing
            // before the empty-rule short-circuit preserves behavior.
            await _repository.RemoveRuleTagsAsync(scopeToTagName, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            var ruleTagNames = activeRules
                .Select(r => r.TagName.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (scopeToTagName != null)
                ruleTagNames = ruleTagNames.Where(n => n == scopeToTagName).ToList();

            if (ruleTagNames.Count == 0)
            {
                _cache.Invalidate();
                return new ReapplyRulesResponse { PhotosUpdated = 0 };
            }

            var occupied = await _repository.GetOccupiedTagPairsAsync(scopeToTagName, cancellationToken);
            var tagIdsByName = await _repository.GetOrCreateTagsAsync(ruleTagNames, cancellationToken);
            var photos = await _repository.GetAllPhotosAsync(cancellationToken);

            var addedPairs = new HashSet<(int PhotoId, int TagId)>();
            var newPhotoTags = new List<PhotoTag>();
            var now = DateTime.UtcNow;
            var photosUpdated = 0;

            foreach (var photo in photos)
            {
                var allMatchingTagNames = TagRuleMatcher.GetMatchingTags(photo.FolderPath, photo.FileName, activeRules);
                var matchingTagNames = scopeToTagName != null
                    ? (IReadOnlyList<string>)allMatchingTagNames.Where(n => n == scopeToTagName).ToList()
                    : allMatchingTagNames;

                if (matchingTagNames.Count == 0)
                    continue;

                var tagsUpdated = false;
                foreach (var tagName in matchingTagNames)
                {
                    if (!tagIdsByName.TryGetValue(tagName, out var tagId))
                        continue;

                    var pair = (photo.Id, tagId);
                    if (!addedPairs.Add(pair))
                        continue;

                    if (occupied.Contains(pair))
                        continue;

                    newPhotoTags.Add(new PhotoTag
                    {
                        PhotoId = photo.Id,
                        TagId = tagId,
                        Source = PhotoTagSource.Rule,
                        CreatedAt = now,
                    });
                    tagsUpdated = true;
                }

                if (tagsUpdated)
                    photosUpdated++;
            }

            await _repository.AddPhotoTagsAsync(newPhotoTags, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new ReapplyRulesResponse { PhotosUpdated = photosUpdated };
        }
    }
}
