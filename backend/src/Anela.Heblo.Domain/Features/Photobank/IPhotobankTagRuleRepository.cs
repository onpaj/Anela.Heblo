using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankTagRuleRepository
    {
        Task<List<TagRule>> GetRulesAsync(CancellationToken cancellationToken);
        Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken);
        Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken);
        Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken);
        Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken);
        Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
