using System.Diagnostics.CodeAnalysis;
using Anela.Heblo.Application.Features.Photobank.Contracts;

namespace Anela.Heblo.Application.Features.Photobank.Services;

public interface IPhotobankTagsCache
{
    bool TryGet([NotNullWhen(true)] out IReadOnlyList<TagWithCountDto>? tags);
    void Set(IReadOnlyList<TagWithCountDto> tags);
    void Invalidate();
}
