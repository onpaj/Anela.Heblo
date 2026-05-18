using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.DataQuality;

public class DqtDriftResult : Entity<Guid>
{
    public Guid DqtRunId { get; private set; }
    public DqtTestType TestType { get; private set; }
    public string EntityKey { get; private set; } = string.Empty;
    public int MismatchCode { get; private set; }
    public string? HebloValue { get; private set; }
    public string? ShoptetValue { get; private set; }
    public string? Details { get; private set; }

    private DqtDriftResult() { }

    public static DqtDriftResult Create(
        Guid dqtRunId,
        DqtTestType testType,
        string entityKey,
        int mismatchCode,
        string? hebloValue,
        string? shoptetValue,
        string? details)
    {
        return new DqtDriftResult
        {
            Id = Guid.NewGuid(),
            DqtRunId = dqtRunId,
            TestType = testType,
            EntityKey = entityKey,
            MismatchCode = mismatchCode,
            HebloValue = hebloValue,
            ShoptetValue = shoptetValue,
            Details = details
        };
    }
}
