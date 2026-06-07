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
        public string? CreatedByUsername { get; set; }

        [MaxLength(100)]
        public string? ModifiedByUserId { get; set; }

        [MaxLength(100)]
        public string? ModifiedByUsername { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        [MaxLength(100)]
        public string? DeletedByUserId { get; set; }

        [MaxLength(100)]
        public string? DeletedByUsername { get; set; }

        // Navigation properties
        public virtual ICollection<JournalEntryProduct> ProductAssociations { get; set; } = new List<JournalEntryProduct>();
        public virtual ICollection<JournalEntryTagAssignment> TagAssignments { get; set; } = new List<JournalEntryTagAssignment>();

        // Domain methods
        public void AssociateWithProduct(string productCode)
        {
            var normalized = NormalizeProductCode(productCode);

            if (ProductAssociations.Any(pa => pa.ProductCodePrefix == normalized))
                return; // Already associated

            ProductAssociations.Add(new JournalEntryProduct
            {
                JournalEntryId = Id,
                ProductCodePrefix = normalized
            });
        }

        public void ReplaceProductAssociations(IEnumerable<string>? productCodes)
        {
            // Validate entire input set before any mutation (state preserved on invalid input).
            var targetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (productCodes != null)
            {
                foreach (var raw in productCodes)
                {
                    targetCodes.Add(NormalizeProductCode(raw));
                }
            }

            var toRemove = ProductAssociations
                .Where(pa => !targetCodes.Contains(pa.ProductCodePrefix))
                .ToList();
            foreach (var association in toRemove)
            {
                ProductAssociations.Remove(association);
            }

            var existingCodes = new HashSet<string>(
                ProductAssociations.Select(pa => pa.ProductCodePrefix),
                StringComparer.OrdinalIgnoreCase);
            foreach (var code in targetCodes)
            {
                if (existingCodes.Contains(code))
                    continue;

                ProductAssociations.Add(new JournalEntryProduct
                {
                    JournalEntryId = Id,
                    ProductCodePrefix = code
                });
            }
        }

        private static string NormalizeProductCode(string? productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentException("Product code cannot be empty", nameof(productCode));

            return productCode.Trim().ToUpperInvariant();
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

        public void ReplaceTagAssignments(IEnumerable<int>? tagIds)
        {
            var targetIds = tagIds != null
                ? new HashSet<int>(tagIds)
                : new HashSet<int>();

            var toRemove = TagAssignments
                .Where(ta => !targetIds.Contains(ta.TagId))
                .ToList();
            foreach (var assignment in toRemove)
            {
                TagAssignments.Remove(assignment);
            }

            var existingIds = new HashSet<int>(TagAssignments.Select(ta => ta.TagId));
            foreach (var tagId in targetIds)
            {
                if (existingIds.Contains(tagId))
                    continue;

                TagAssignments.Add(new JournalEntryTagAssignment
                {
                    JournalEntryId = Id,
                    TagId = tagId
                });
            }
        }

        public void Update(string? title, string content, DateTime entryDate, string userId, string username)
        {
            Title = title?.Trim();
            Content = content.Trim();
            EntryDate = entryDate.Date;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
            ModifiedByUsername = username;
        }

        public void SoftDelete(string userId, string username)
        {
            IsDeleted = true;
            DeletedAt = DateTime.UtcNow;
            DeletedByUserId = userId;
            DeletedByUsername = username;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
            ModifiedByUsername = username;
        }

        public bool IsAssociatedWithProduct(string productCode)
        {
            return ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix));
        }
    }
}