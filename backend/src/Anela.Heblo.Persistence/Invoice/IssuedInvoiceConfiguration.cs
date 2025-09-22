using Anela.Heblo.Domain.Features.Invoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Invoice;

/// <summary>
/// Entity configuration for IssuedInvoice
/// </summary>
public class IssuedInvoiceConfiguration : IEntityTypeConfiguration<IssuedInvoice>
{
    public void Configure(EntityTypeBuilder<IssuedInvoice> builder)
    {
        builder.ToTable("IssuedInvoices");
        
        // Primary key
        builder.HasKey(e => e.ExternalId);
        
        // Required properties
        builder.Property(e => e.ExternalId)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(e => e.InvoiceDate)
            .IsRequired();
            
        builder.Property(e => e.Amount)
            .IsRequired()
            .HasPrecision(18, 2);
            
        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);
            
        builder.Property(e => e.CustomerName)
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(e => e.CreatedAt)
            .IsRequired();
        
        // Optional properties
        builder.Property(e => e.CustomerEmail)
            .HasMaxLength(200);
            
        builder.Property(e => e.Description)
            .HasMaxLength(1000);
        
        // Relationships
        builder.HasMany(e => e.ImportAttempts)
            .WithOne()
            .HasForeignKey(ia => ia.ExternalInvoiceId)
            .HasPrincipalKey(e => e.ExternalId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Indexes
        builder.HasIndex(e => e.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("IX_IssuedInvoice_InvoiceNumber");
            
        builder.HasIndex(e => e.InvoiceDate)
            .HasDatabaseName("IX_IssuedInvoice_InvoiceDate");
            
        builder.HasIndex(e => e.CustomerName)
            .HasDatabaseName("IX_IssuedInvoice_CustomerName");
            
        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("IX_IssuedInvoice_CreatedAt");
    }
}