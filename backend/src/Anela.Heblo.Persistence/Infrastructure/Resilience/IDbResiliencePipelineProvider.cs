using Polly;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

public interface IDbResiliencePipelineProvider
{
    ResiliencePipeline Pipeline { get; }
}
