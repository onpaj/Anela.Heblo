using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Invoices;

/// <summary>
/// EF Core configuration for IssuedInvoiceSyncData entity
/// Maps to existing dbo.IssuedInvoiceSyncData table
/// </summary>
public class IssuedInvoiceSyncDataConfiguration : IEntityTypeConfiguration<IssuedInvoiceSyncData>
{
    public void Configure(EntityTypeBuilder<IssuedInvoiceSyncData> builder)
    {
        builder.ToTable("IssuedInvoiceSyncData", "dbo");
        builder.HasKey(e => e.Id);

        // Configure properties
        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(e => e.Data)
            .HasColumnName("Data");

        builder.Property(e => e.IsSuccess)
            .HasColumnName("IsSuccess")
            .IsRequired();

        builder.Property(e => e.SyncTime)
            .HasColumnName("SyncTime")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(e => e.IssuedInvoiceId)
            .HasColumnName("IssuedInvoiceId")
            .IsRequired();

        // Configure owned entity for Error
        builder.OwnsOne(e => e.Error, errorBuilder =>
        {
            errorBuilder.Property(e => e.ErrorType)
                .HasColumnName("Error_ErrorType")
                .HasConversion<int?>();

            errorBuilder.Property(e => e.Message)
                .HasColumnName("Error_Message");
        });
    }
}