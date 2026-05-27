## Module
Article

## Finding
`GenerateArticleResponse.cs` defines only one field:

```csharp
public sealed class GenerateArticleResponse : BaseResponse
{
    public Guid ArticleId { get; set; }
    // ...
}
```

The feature spec (section 7 and acceptance criteria, `docs/features/article-generation.md`) requires:

```csharp
public class GenerateArticleResponse : BaseResponse
{
    public Guid? ArticleId { get; set; }
    public string? HangfireJobId { get; set; }
    public ArticleStatus Status { get; set; }
}
```

Acceptance criterion:
> `POST /api/Articles/generate` returns `{articleId, jobId, Status: Queued}` within 500ms

In `GenerateArticleHandler.cs:53`, `IBackgroundJobClient.Enqueue()` returns a `string` job ID which is discarded:

```csharp
_backgroundJobClient.Enqueue<GenerateArticleJob>(j => j.RunAsync(article.Id, CancellationToken.None));
// job ID return value thrown away
return new GenerateArticleResponse { ArticleId = article.Id };
```

## Why it matters
- Clients have no way to correlate a generation request with the Hangfire job without polling the article status. With `HangfireJobId`, operators can look up jobs directly in the Hangfire dashboard or cancel them programmatically.
- The `Status: Queued` field signals to the frontend that polling should begin immediately; its absence requires the client to infer the initial state.
- This is a named acceptance criterion in the spec — omitting it means the feature is shipped incomplete per its own definition of done.

## Suggested fix
Capture the job ID and extend the response class:

```csharp
// GenerateArticleResponse.cs — add two properties
public string? HangfireJobId { get; set; }
public ArticleStatus Status { get; set; }

// GenerateArticleHandler.cs — capture return value
var jobId = _backgroundJobClient.Enqueue<GenerateArticleJob>(
    j => j.RunAsync(article.Id, CancellationToken.None));

return new GenerateArticleResponse
{
    ArticleId = article.Id,
    HangfireJobId = jobId,
    Status = ArticleStatus.Queued,
};
```

---
_Filed by daily arch-review routine on 2026-05-25._