namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayoutPersistenceException : Exception
{
    public string? SqlState { get; }

    public GridLayoutPersistenceException(string message, string? sqlState, Exception inner)
        : base(message, inner)
    {
        SqlState = sqlState;
    }
}
