using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalEntry : IEntity<int>
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        [Required]
        [MaxLength(10000)]
        public string Content { get; set; } = null!;

        [Required]
        public DateTime EntryDate { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime ModifiedAt { get; set; }

        [Required]
        [MaxLength(100)]
        public string CreatedByUserId { get; set; } = null!;

        [MaxLength(100)]
        public string? ModifiedByUserId { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        [MaxLength(100)]
        public string? DeletedByUserId { get; set; }

        // Navigation properties
        public virtual ICollection<JournalEntryProduct> ProductAssociations { get; set; } = new List<JournalEntryProduct>();
        public virtual ICollection<JournalEntryProductFamily> ProductFamilyAssociations { get; set; } = new List<JournalEntryProductFamily>();
        public virtual ICollection<JournalEntryTagAssignment> TagAssignments { get; set; } = new List<JournalEntryTagAssignment>();

        // Domain methods
        public void AssociateWithProduct(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentException("Product code cannot be empty", nameof(productCode));

            if (ProductAssociations.Any(pa => pa.ProductCode == productCode))
                return; // Already associated

            ProductAssociations.Add(new JournalEntryProduct
            {
                JournalEntryId = Id,
                ProductCode = productCode.Trim().ToUpperInvariant()
            });
        }

        public void AssociateWithProductFamily(string productCodePrefix)
        {
            if (string.IsNullOrWhiteSpace(productCodePrefix))
                throw new ArgumentException("Product code prefix cannot be empty", nameof(productCodePrefix));

            if (ProductFamilyAssociations.Any(pfa => pfa.ProductCodePrefix == productCodePrefix))
                return; // Already associated

            ProductFamilyAssociations.Add(new JournalEntryProductFamily
            {
                JournalEntryId = Id,
                ProductCodePrefix = productCodePrefix.Trim().ToUpperInvariant()
            });
        }

        public void AssignTag(int tagId)
        {
            if (TagAssignments.Any(ta => ta.TagId == tagId))
                return; // Already assigned

            TagAssignments.Add(new JournalEntryTagAssignment
            {
                JournalEntryId = Id,
                TagId = tagId
            });
        }

        public void SoftDelete(string userId)
        {
            IsDeleted = true;
            DeletedAt = DateTime.UtcNow;
            DeletedByUserId = userId;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
        }

        public bool IsAssociatedWithProduct(string productCode)
        {
            return ProductAssociations.Any(pa => pa.ProductCode == productCode) ||
                   ProductFamilyAssociations.Any(pfa => productCode.StartsWith(pfa.ProductCodePrefix));
        }
    }
}