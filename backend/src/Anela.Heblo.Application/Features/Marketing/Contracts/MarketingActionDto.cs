using System;
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class MarketingActionDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string ActionType { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public string? CreatedByUsername { get; set; }
        public string? ModifiedByUserId { get; set; }
        public string? ModifiedByUsername { get; set; }
        public List<string> AssociatedProducts { get; set; } = new();
        public List<MarketingActionFolderLinkDto> FolderLinks { get; set; } = new();
        public string OutlookSyncStatus { get; set; } = "NotSynced";
        public string? OutlookEventId { get; set; }

        public static MarketingActionDto FromEntity(MarketingAction action) =>
            new()
            {
                Id = action.Id,
                Title = action.Title,
                Description = action.Description,
                ActionType = action.ActionType.ToString(),
                StartDate = action.StartDate,
                EndDate = action.EndDate,
                CreatedAt = action.CreatedAt,
                ModifiedAt = action.ModifiedAt,
                CreatedByUserId = action.CreatedByUserId,
                CreatedByUsername = action.CreatedByUsername,
                ModifiedByUserId = action.ModifiedByUserId,
                ModifiedByUsername = action.ModifiedByUsername,
                AssociatedProducts = action.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                FolderLinks = action.FolderLinks
                    .Select(fl => new MarketingActionFolderLinkDto
                    {
                        FolderKey = fl.FolderKey,
                        FolderType = fl.FolderType.ToString(),
                    })
                    .ToList(),
                OutlookSyncStatus = action.OutlookSyncStatus.ToString(),
                OutlookEventId = action.OutlookEventId,
            };
    }
}
