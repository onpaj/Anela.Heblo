using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Persistence.InvoiceClassification;

public class ClassificationHistoryConfiguration : IEntityTypeConfiguration<ClassificationHistory>
{
    public void Configure(EntityTypeBuilder<ClassificationHistory> builder)
    {
        builder.ToTable("ClassificationHistory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AbraInvoiceId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ClassificationRuleId)
            .IsRequired(false);

        builder.Property(x => x.Result)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.AccountingPrescription)
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(x => x.Timestamp)
            .IsRequired()
            .HasColumnType("timestamp");

        builder.Property(x => x.ProcessedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasOne(x => x.ClassificationRule)
            .WithMany()
            .HasForeignKey(x => x.ClassificationRuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.AbraInvoiceId)
            .HasDatabaseName("IX_ClassificationHistory_AbraInvoiceId");

        builder.HasIndex(x => x.Timestamp)
            .HasDatabaseName("IX_ClassificationHistory_Timestamp");

        builder.HasIndex(x => x.Result)
            .HasDatabaseName("IX_ClassificationHistory_Result");
    }
}