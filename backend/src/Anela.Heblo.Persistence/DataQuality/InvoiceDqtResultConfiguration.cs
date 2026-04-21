using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.DataQuality;

public class InvoiceDqtResultConfiguration : IEntityTypeConfiguration<InvoiceDqtResult>
{
    public void Configure(EntityTypeBuilder<InvoiceDqtResult> builder)
    {
        builder.ToTable("invoice_dqt_results");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(e => e.DqtRunId)
            .HasColumnName("dqt_run_id")
            .IsRequired();

        builder.Property(e => e.InvoiceCode)
            .HasColumnName("invoice_code")
            .IsRequired();

        builder.Property(e => e.MismatchType)
            .HasColumnName("mismatch_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ShoptetValue)
            .HasColumnName("shoptet_value");

        builder.Property(e => e.FlexiValue)
            .HasColumnName("flexi_value");

        builder.Property(e => e.Details)
            .HasColumnName("details")
            .HasMaxLength(4000);

        builder.HasIndex(e => e.DqtRunId)
            .HasDatabaseName("IX_invoice_dqt_results_dqt_run_id");

        builder.HasIndex(e => e.InvoiceCode)
            .HasDatabaseName("IX_invoice_dqt_results_invoice_code");
    }
}
