using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainArticleSource = Anela.Heblo.Domain.Features.Article.ArticleSource;

namespace Anela.Heblo.Persistence.Features.Article;

public class ArticleSourceConfiguration : IEntityTypeConfiguration<DomainArticleSource>
{
    public void Configure(EntityTypeBuilder<DomainArticleSource> builder)
    {
        builder.ToTable("ArticleSources");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ArticleId).IsRequired();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Url).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Confidence).IsRequired(false);
        builder.Property(x => x.KnowledgeBaseChunkId).IsRequired(false);
        builder.Property(x => x.Excerpt).IsRequired(false).HasMaxLength(10000);
        builder.Property(x => x.ValidationNote).IsRequired(false).HasMaxLength(10000);

        builder.HasIndex(x => x.ArticleId)
            .HasDatabaseName("IX_ArticleSources_ArticleId");
    }
}
