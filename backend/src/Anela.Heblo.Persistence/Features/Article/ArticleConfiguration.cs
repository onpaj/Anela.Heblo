using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;
using DomainArticleSource = Anela.Heblo.Domain.Features.Article.ArticleSource;

namespace Anela.Heblo.Persistence.Features.Article;

public class ArticleConfiguration : IEntityTypeConfiguration<DomainArticle>
{
    public void Configure(EntityTypeBuilder<DomainArticle> builder)
    {
        builder.ToTable("Articles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Topic).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Scope).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Audience).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.Angle).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.Length).IsRequired().HasMaxLength(50);
        builder.Property(x => x.LanguageNote).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.StyleGuideDriveId).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.StyleGuideItemPath).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.Title).IsRequired(false);
        builder.Property(x => x.HtmlContent).IsRequired(false);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ErrorMessage).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.RequestedBy).IsRequired(false).HasMaxLength(200);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.GeneratedAt).IsRequired(false);
        builder.Property(x => x.PrecisionScore).IsRequired(false);
        builder.Property(x => x.StyleScore).IsRequired(false);
        builder.Property(x => x.FeedbackComment).IsRequired(false).HasColumnType("text");

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("IX_Articles_Status_CreatedAt");

        builder.HasIndex(x => x.PrecisionScore)
            .HasDatabaseName("IX_Articles_PrecisionScore")
            .HasFilter("\"PrecisionScore\" IS NOT NULL");

        builder.HasMany(x => x.Sources)
            .WithOne()
            .HasForeignKey(x => x.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
