using System;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Marketing
{
    public class MarketingActionFolderLink
    {
        public int MarketingActionId { get; set; }

        [Required]
        [MaxLength(100)]
        public string FolderKey { get; set; } = null!;

        public MarketingFolderType FolderType { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public virtual MarketingAction MarketingAction { get; set; } = null!;
    }
}
