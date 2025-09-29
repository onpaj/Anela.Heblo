using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Invoices;

/// <summary>
/// EF Core configuration for IssuedInvoice entity
/// </summary>
public class IssuedInvoiceConfiguration : IEntityTypeConfiguration<IssuedInvoice>
{
    public void Configure(EntityTypeBuilder<IssuedInvoice> builder)
    {
        builder.ToTable("IssuedInvoice", "dbo");
        
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(e => e.InvoiceDate)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            
        builder.Property(e => e.LastSyncTime)
            .IsRequired(false)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime() : (DateTime?)null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
            
        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            
        builder.Property(e => e.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            
        // Create index for efficient date range queries
        builder.HasIndex(e => e.InvoiceDate)
            .HasDatabaseName("IX_IssuedInvoice_InvoiceDate");
            
        builder.HasIndex(e => e.LastSyncTime)
            .HasDatabaseName("IX_IssuedInvoice_LastSyncTime");
            
        // Unique constraint on Code to prevent duplicates
        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasDatabaseName("IX_IssuedInvoice_Code_Unique");
    }
}