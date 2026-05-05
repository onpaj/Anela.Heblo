using System;
using System.Collections.Generic;

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
    }
}
