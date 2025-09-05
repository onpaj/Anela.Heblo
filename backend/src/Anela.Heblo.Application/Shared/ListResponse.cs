namespace Anela.Heblo.Application.Shared;

/// <summary>
/// Generic list response wrapper
/// </summary>
public class ListResponse<T> : BaseResponse
{
    public List<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
}