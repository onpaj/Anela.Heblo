using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalEntryTag : IEntity<int>
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = null!;

        [MaxLength(7)]
        public string Color { get; set; } = "#6B7280"; // Default gray

        public DateTime CreatedAt { get; set; }

        [Required]
        [MaxLength(100)]
        public string CreatedByUserId { get; set; } = null!;

        // Navigation properties
        public virtual ICollection<JournalEntryTagAssignment> TagAssignments { get; set; } = new List<JournalEntryTagAssignment>();
    }
}