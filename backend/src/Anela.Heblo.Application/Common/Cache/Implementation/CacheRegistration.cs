using Anela.Heblo.Application.Common.Cache.Abstractions;

namespace Anela.Heblo.Application.Common.Cache.Implementation;

public class CacheRegistration
{
    public string Name { get; set; } = string.Empty;
    public ICacheRefreshConfiguration Configuration { get; set; } = null!;
    public object CacheService { get; set; } = null!;
    public Type CacheServiceType { get; set; } = null!;
}