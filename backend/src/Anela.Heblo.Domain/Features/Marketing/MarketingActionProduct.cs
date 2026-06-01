using System;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Marketing
{
    public class MarketingActionProduct
    {
        public int MarketingActionId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProductCodePrefix { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public virtual MarketingAction MarketingAction { get; set; } = null!;
    }
}
