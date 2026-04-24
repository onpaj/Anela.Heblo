using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.DataQuality;

public class InvoiceDqtResultConfiguration : IEntityTypeConfiguration<InvoiceDqtResult>
{
    public void Configure(EntityTypeBuilder<InvoiceDqtResult> builder)
    {
        builder.ToTable("InvoiceDqtResults", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.DqtRunId)
            .IsRequired();

        builder.Property(e => e.InvoiceCode)
            .IsRequired();

        builder.Property(e => e.MismatchType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ShoptetValue);

        builder.Property(e => e.FlexiValue);

        builder.Property(e => e.Details)
            .HasMaxLength(4000);

        builder.HasIndex(e => e.DqtRunId)
            .HasDatabaseName("IX_InvoiceDqtResults_DqtRunId");

        builder.HasIndex(e => e.InvoiceCode)
            .HasDatabaseName("IX_InvoiceDqtResults_InvoiceCode");
    }
}
