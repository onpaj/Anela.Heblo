using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Persistence.InvoiceClassification;

public class ClassificationRuleConfiguration : IEntityTypeConfiguration<ClassificationRule>
{
    public void Configure(EntityTypeBuilder<ClassificationRule> builder)
    {
        builder.ToTable("ClassificationRules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.RuleTypeIdentifier)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Pattern)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.AccountingPrescription)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Order)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp");

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => x.Order)
            .IsUnique();

        builder.HasIndex(x => new { x.RuleTypeIdentifier, x.Pattern })
            .HasDatabaseName("IX_ClassificationRules_RuleTypeIdentifier_Pattern");
    }
}