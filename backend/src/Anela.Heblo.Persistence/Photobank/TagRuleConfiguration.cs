using Anela.Heblo.Domain.Features.Photobank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Photobank
{
    public class TagRuleConfiguration : IEntityTypeConfiguration<TagRule>
    {
        public void Configure(EntityTypeBuilder<TagRule> builder)
        {
            builder.ToTable("PhotobankTagRules", "public");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.PathPattern).HasMaxLength(1000).IsRequired();
            builder.Property(x => x.TagName).HasMaxLength(100).IsRequired();
            builder.HasIndex(x => new { x.IsActive, x.SortOrder }).HasDatabaseName("IX_PhotobankTagRules_Active_SortOrder");
        }
    }
}
