using Anela.Heblo.Domain.Features.Invoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Invoice;

/// <summary>
/// Entity configuration for ImportAttempt
/// Maps to existing IssuedInvoiceSyncData table for backward compatibility
/// </summary>
public class ImportAttemptConfiguration : IEntityTypeConfiguration<ImportAttempt>
{
    public void Configure(EntityTypeBuilder<ImportAttempt> builder)
    {
        // Map to existing table for backward compatibility
        builder.ToTable("IssuedInvoiceSyncData");
        
        // Primary key
        builder.HasKey(e => e.Id);
        
        // Required properties
        builder.Property(e => e.ExternalInvoiceId)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.AttemptedAt)
            .IsRequired();
            
        builder.Property(e => e.IsSuccess)
            .IsRequired();
        
        // Optional properties
        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);
            
        builder.Property(e => e.ImportId)
            .HasMaxLength(100);
            
        builder.Property(e => e.InvoiceNumber)
            .HasMaxLength(50);
            
        builder.Property(e => e.Amount)
            .HasPrecision(18, 2);
            
        builder.Property(e => e.Currency)
            .HasMaxLength(3);
        
        // Indexes for performance
        builder.HasIndex(e => e.ExternalInvoiceId)
            .HasDatabaseName("IX_ImportAttempt_ExternalInvoiceId");
            
        builder.HasIndex(e => new { e.ExternalInvoiceId, e.IsSuccess })
            .HasDatabaseName("IX_ImportAttempt_ExternalInvoiceId_IsSuccess");
            
        builder.HasIndex(e => e.AttemptedAt)
            .HasDatabaseName("IX_ImportAttempt_AttemptedAt");
    }
}